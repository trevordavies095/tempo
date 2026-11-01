using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Backfills <c>device_lap</c> WorkoutSplit rows from FIT LapMesg data (JSON <c>laps</c>
/// or reparsed <see cref="Workout.RawFileData"/>). Idempotent via top-level
/// <c>lapsBackfill</c> markers. Does not rewrite distance splits or track geometry.
/// </summary>
public class DeviceLapBackfillService
{
    public const int BatchSize = 200;
    public const string LapsBackfillKey = "lapsBackfill";
    public const string MarkerApplied = "applied";
    public const string MarkerSkippedSingle = "skipped_single";
    public const string MarkerSkippedEmpty = "skipped_empty";
    public const string MarkerSkippedCrop = "skipped_crop";
    public const string MarkerAbsent = "absent";
    public const string MarkerUnparseable = "unparseable";

    /// <summary>
    /// Second-step marker: device_lap rows carry FIT intensity / trigger / step index
    /// (or were judged unfillable). Independent of <see cref="LapsBackfillKey"/>.
    /// </summary>
    public const string LapAnnotationsBackfillKey = "lapAnnotationsBackfill";
    public const string MarkerSkippedMismatch = "skipped_mismatch";
    public const string MarkerUnrecoverable = "unrecoverable";

    /// <summary>
    /// Annotation-step candidate scan: workouts with device laps and an unstamped JSON object.
    /// Rows without FIT bytes are still candidates so they get stamped <see cref="MarkerUnrecoverable"/>.
    /// </summary>
    public const string PostgresAnnotationCandidateSql =
        """
        SELECT w."Id" AS "Value"
        FROM "Workouts" AS w
        WHERE EXISTS (
            SELECT 1 FROM "WorkoutSplits" AS s
            WHERE s."WorkoutId" = w."Id" AND s."Kind" = 'device_lap'
        )
        AND w."RawFitData" IS NOT NULL
        AND jsonb_typeof(w."RawFitData") = 'object'
        AND w."RawFitData"->>'lapAnnotationsBackfill' IS NULL
        ORDER BY w."Id"
        """;

    /// <summary>Substring used by tests to assert a stamp was or was not written.</summary>
    public const string LapsBackfillMarkerPrefix = "\"lapsBackfill\":";
    private const string UnparseableStampJson = """{"lapsBackfill":"unparseable"}""";

    /// <summary>
    /// Production candidate scan. jsonb path — do not LIKE compact <c>"lapsBackfill":…</c>.
    /// FIT bytes without JSON stay eligible until stamped.
    /// </summary>
    public const string PostgresCandidateSql =
        """
        SELECT w."Id" AS "Value"
        FROM "Workouts" AS w
        WHERE NOT EXISTS (
            SELECT 1 FROM "WorkoutSplits" AS s
            WHERE s."WorkoutId" = w."Id" AND s."Kind" = 'device_lap'
        )
        AND w."RawFitData"->>'lapsBackfill' IS NULL
        AND (
          (
            w."RawFitData" IS NOT NULL
            AND w."RawFitData"::text <> ''
          )
          OR (
            w."RawFileData" IS NOT NULL
            AND length(w."RawFileData") > 0
            AND (
              lower(coalesce(w."RawFileType", '')) = 'fit'
              OR lower(coalesce(w."RawFileName", '')) LIKE '%.fit'
              OR lower(coalesce(w."RawFileName", '')) LIKE '%.fit.gz'
            )
          )
        )
        ORDER BY w."Id"
        """;

    private readonly TempoDbContext _db;
    private readonly FitParserService _fitParser;
    private readonly SplitHeartRateService _splitHeartRate;
    private readonly ILogger<DeviceLapBackfillService> _logger;

    public DeviceLapBackfillService(
        TempoDbContext db,
        FitParserService fitParser,
        SplitHeartRateService splitHeartRate,
        ILogger<DeviceLapBackfillService> logger)
    {
        _db = db;
        _fitParser = fitParser;
        _splitHeartRate = splitHeartRate;
        _logger = logger;
    }

    /// <summary>
    /// Processes candidate workouts. Returns the number that were stamped or received laps.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var candidateIds = await LoadCandidateIdsAsync(cancellationToken);

        var total = candidateIds.Count;
        if (total == 0)
        {
            _logger.LogInformation("Device lap backfill: {Processed} of {Total}", 0, 0);
            return 0;
        }

        var processed = 0;
        foreach (var workoutId in candidateIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (await FillWorkoutAsync(workoutId, cancellationToken))
                {
                    processed++;
                    if (processed % BatchSize == 0 || processed == total)
                    {
                        _logger.LogInformation(
                            "Device lap backfill: {Processed} of {Total}",
                            processed,
                            total);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Device lap backfill failed for workout {WorkoutId}", workoutId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        if (processed > 0 && processed % BatchSize != 0 && processed != total)
        {
            _logger.LogInformation("Device lap backfill: {Processed} of {Total}", processed, total);
        }

        return processed;
    }

    private async Task<bool> FillWorkoutAsync(Guid workoutId, CancellationToken cancellationToken)
    {
        var workout = await _db.Workouts
            .FirstAsync(w => w.Id == workoutId, cancellationToken);

        var hasDeviceLaps = await _db.WorkoutSplits.AnyAsync(
            s => s.WorkoutId == workoutId && s.Kind == WorkoutSplitKinds.DeviceLap,
            cancellationToken);
        if (hasDeviceLaps)
        {
            return false;
        }

        if (IsLapsBackfillComplete(workout.RawFitData))
        {
            return false;
        }

        var hasFitJson = !string.IsNullOrEmpty(workout.RawFitData);
        var hasFitBytes = workout.RawFileData != null && workout.RawFileData.Length > 0 && LooksLikeFitFile(workout);
        if (!hasFitJson && !hasFitBytes)
        {
            return false;
        }

        if (hasFitJson && !TryGetPatchableObject(workout.RawFitData!, out _))
        {
            _logger.LogError(
                new InvalidOperationException("RawFitData is not a JSON object"),
                "Device lap backfill failed for workout {WorkoutId}",
                workoutId);
            workout.RawFitData = UnparseableStampJson;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Ensure we have a JSON object to stamp / patch.
        if (!hasFitJson)
        {
            workout.RawFitData = "{}";
        }

        var sessionElapsed = TryReadSessionElapsedFromRawFitJson(workout.RawFitData);
        IReadOnlyList<DeviceLapSummary>? candidates = TryReadLapsFromRawFitJson(workout.RawFitData);
        var hadLapMessagesInJson = candidates != null;
        // Legacy JSON laps lack intensity / step; only a fresh FIT parse yields complete annotations.
        var candidatesFromFitParse = false;

        if ((!sessionElapsed.HasValue || candidates == null) && hasFitBytes)
        {
            FitParserService.FitParseResult parseResult;
            try
            {
                using var stream = new MemoryStream(workout.RawFileData!);
                var isGzipped = workout.RawFileName?.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase) == true;
                parseResult = isGzipped
                    ? _fitParser.ParseGzippedFit(stream)
                    : _fitParser.ParseFit(stream);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Device lap backfill failed for workout {WorkoutId}", workoutId);
                workout.RawFitData = StampMarker(workout.RawFitData!, MarkerUnparseable);
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }

            sessionElapsed ??= TryReadSessionElapsedFromRawFitJson(parseResult.RawFitDataJson);
            if (candidates == null)
            {
                candidates = parseResult.Laps;
                candidatesFromFitParse = true;
                hadLapMessagesInJson = parseResult.Laps.Count > 0;
                workout.RawFitData = PatchLapsArray(workout.RawFitData!, parseResult.RawFitDataJson);
            }
        }

        candidates ??= Array.Empty<DeviceLapSummary>();

        if (sessionElapsed.HasValue &&
            Math.Abs(sessionElapsed.Value - workout.DurationS) > 1)
        {
            workout.RawFitData = StampMarker(workout.RawFitData!, MarkerSkippedCrop);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var kept = candidates.Where(DeviceLapMapper.ShouldKeep).ToList();
        if (kept.Count >= 2)
        {
            var startedAtUtc = workout.StartedAt.Kind == DateTimeKind.Utc
                ? workout.StartedAt
                : DateTime.SpecifyKind(workout.StartedAt, DateTimeKind.Utc);
            var deviceLaps = DeviceLapMapper.ToDeviceLapSplits(candidates, startedAtUtc, workout.Id);
            var series = await _db.WorkoutTimeSeries
                .Where(ts => ts.WorkoutId == workout.Id)
                .OrderBy(ts => ts.ElapsedSeconds)
                .ToListAsync(cancellationToken);
            _splitHeartRate.ApplyToSplits(deviceLaps, series);
            DeviceLapMapper.OverlayDeviceAvgHeartRate(deviceLaps, candidates);
            _db.WorkoutSplits.AddRange(deviceLaps);
            workout.RawFitData = StampMarker(workout.RawFitData!, MarkerApplied);
            if (candidatesFromFitParse)
            {
                workout.RawFitData = StampMarker(workout.RawFitData, MarkerApplied, LapAnnotationsBackfillKey);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (kept.Count == 1)
        {
            workout.RawFitData = StampMarker(workout.RawFitData!, MarkerSkippedSingle);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        // kept.Count == 0
        if (candidates.Count > 0 || hadLapMessagesInJson)
        {
            workout.RawFitData = StampMarker(workout.RawFitData!, MarkerSkippedEmpty);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        workout.RawFitData = StampMarker(workout.RawFitData!, MarkerAbsent);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool LooksLikeFitFile(Workout workout)
    {
        if (string.Equals(workout.RawFileType, "fit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = workout.RawFileName;
        if (string.IsNullOrEmpty(name))
        {
            return !string.IsNullOrEmpty(workout.RawFitData);
        }

        return name.EndsWith(".fit", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryReadSessionElapsedFromRawFitJson(string? rawFitData)
    {
        if (string.IsNullOrEmpty(rawFitData))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(rawFitData);
        if (!doc.RootElement.TryGetProperty("session", out var session) ||
            session.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!session.TryGetProperty("totalElapsedTime", out var elapsed))
        {
            return null;
        }

        return WorkoutClocks.SecondsFromJsonNumber(elapsed);
    }

    /// <summary>
    /// Returns null when the <c>laps</c> property is missing; empty list when present but empty.
    /// </summary>
    private static List<DeviceLapSummary>? TryReadLapsFromRawFitJson(string? rawFitData)
    {
        if (string.IsNullOrEmpty(rawFitData))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(rawFitData);
        if (!doc.RootElement.TryGetProperty("laps", out var laps) ||
            laps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<DeviceLapSummary>(laps.GetArrayLength());
        foreach (var lap in laps.EnumerateArray())
        {
            list.Add(new DeviceLapSummary
            {
                StartTime = ReadOptionalDateTime(lap, "start"),
                Timestamp = ReadOptionalDateTime(lap, "timestamp"),
                DistanceM = ReadDouble(lap, "distance") ?? 0,
                TimerS = ReadOptionalDouble(lap, "timer"),
                ElapsedS = ReadOptionalDouble(lap, "elapsed"),
                AvgHeartRateBpm = ReadOptionalByte(lap, "avgHeartRate"),
                LapTrigger = DeviceLapAnnotations.Normalize(ReadOptionalString(lap, "trigger")),
                Intensity = DeviceLapAnnotations.Normalize(ReadOptionalString(lap, "intensity")),
                StepIndex = lap.TryGetProperty("stepIndex", out var step) &&
                            step.ValueKind == JsonValueKind.Number &&
                            step.TryGetInt32(out var stepValue)
                    ? stepValue
                    : null
            });
        }

        return list;
    }

    private static string? ReadOptionalString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static DateTime? ReadOptionalDateTime(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var s = el.GetString();
        if (string.IsNullOrWhiteSpace(s) ||
            !DateTime.TryParse(s, null,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            return null;
        }

        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    private static double? ReadOptionalDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el))
        {
            return null;
        }

        return ReadDouble(el);
    }

    private static double? ReadDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el))
        {
            return null;
        }

        return ReadDouble(el);
    }

    private static double? ReadDouble(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d))
        {
            return d;
        }

        return null;
    }

    private static byte? ReadOptionalByte(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (el.TryGetByte(out var b))
        {
            return b;
        }

        if (el.TryGetInt32(out var i) && i >= 0 && i <= 255)
        {
            return (byte)i;
        }

        return null;
    }

    private static string PatchLapsArray(string existingJson, string? parsedRawFitDataJson)
    {
        var root = JsonNode.Parse(existingJson)?.AsObject()
            ?? throw new InvalidOperationException("RawFitData is not a JSON object");

        if (!string.IsNullOrEmpty(parsedRawFitDataJson))
        {
            var parsedRoot = JsonNode.Parse(parsedRawFitDataJson)?.AsObject();
            if (parsedRoot?["laps"] is JsonArray parsedLaps)
            {
                root["laps"] = parsedLaps.DeepClone();
            }
            else
            {
                root["laps"] = new JsonArray();
            }
        }

        return root.ToJsonString(JsonUtils.DefaultOptions);
    }

    private static string StampMarker(string existingJson, string value, string key = LapsBackfillKey)
    {
        var root = JsonNode.Parse(existingJson)?.AsObject()
            ?? throw new InvalidOperationException("RawFitData is not a JSON object");

        root[key] = value;
        return root.ToJsonString(JsonUtils.DefaultOptions);
    }

    /// <summary>
    /// Stamps <see cref="LapAnnotationsBackfillKey"/> = applied on freshly parsed FIT JSON so new
    /// imports never become annotation candidates. Returns the input unchanged when it is not a JSON object.
    /// </summary>
    public static string? StampLapAnnotationsApplied(string? rawFitDataJson)
    {
        if (string.IsNullOrEmpty(rawFitDataJson) || !TryGetPatchableObject(rawFitDataJson, out _))
        {
            return rawFitDataJson;
        }

        return StampMarker(rawFitDataJson, MarkerApplied, LapAnnotationsBackfillKey);
    }

    /// <summary>
    /// Second step: fills Intensity / Trigger / StepIndex on existing device_lap rows from reparsed
    /// FIT bytes, in place by Idx. Never inserts, deletes, or rewrites other lap columns.
    /// Returns the number of workouts stamped.
    /// </summary>
    public async Task<int> RunAnnotationsAsync(CancellationToken cancellationToken = default)
    {
        var candidateIds = await _db.Database
            .SqlQueryRaw<Guid>(PostgresAnnotationCandidateSql)
            .ToListAsync(cancellationToken);

        var total = candidateIds.Count;
        var processed = 0;
        foreach (var workoutId in candidateIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (await AnnotateWorkoutAsync(workoutId, cancellationToken))
                {
                    processed++;
                    if (processed % BatchSize == 0 || processed == total)
                    {
                        _logger.LogInformation(
                            "Device lap annotation backfill: {Processed} of {Total}",
                            processed,
                            total);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Device lap annotation backfill failed for workout {WorkoutId}", workoutId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        return processed;
    }

    private async Task<bool> AnnotateWorkoutAsync(Guid workoutId, CancellationToken cancellationToken)
    {
        var workout = await _db.Workouts.FirstAsync(w => w.Id == workoutId, cancellationToken);
        if (string.IsNullOrEmpty(workout.RawFitData) || !TryGetPatchableObject(workout.RawFitData, out _))
        {
            return false;
        }

        var hasFitBytes = workout.RawFileData != null && workout.RawFileData.Length > 0 && LooksLikeFitFile(workout);
        if (!hasFitBytes)
        {
            workout.RawFitData = StampMarker(workout.RawFitData, MarkerUnrecoverable, LapAnnotationsBackfillKey);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        FitParserService.FitParseResult parseResult;
        try
        {
            using var stream = new MemoryStream(workout.RawFileData!);
            var isGzipped = workout.RawFileName?.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase) == true;
            parseResult = isGzipped
                ? _fitParser.ParseGzippedFit(stream)
                : _fitParser.ParseFit(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Device lap annotation backfill failed for workout {WorkoutId}", workoutId);
            workout.RawFitData = StampMarker(workout.RawFitData, MarkerUnparseable, LapAnnotationsBackfillKey);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var sessionElapsed = TryReadSessionElapsedFromRawFitJson(workout.RawFitData)
            ?? TryReadSessionElapsedFromRawFitJson(parseResult.RawFitDataJson);
        if (sessionElapsed.HasValue && Math.Abs(sessionElapsed.Value - workout.DurationS) > 1)
        {
            workout.RawFitData = StampMarker(workout.RawFitData, MarkerSkippedCrop, LapAnnotationsBackfillKey);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var deviceLaps = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workoutId && s.Kind == WorkoutSplitKinds.DeviceLap)
            .OrderBy(s => s.Idx)
            .ToListAsync(cancellationToken);
        var kept = parseResult.Laps.Where(DeviceLapMapper.ShouldKeep).ToList();
        if (kept.Count != deviceLaps.Count)
        {
            workout.RawFitData = StampMarker(workout.RawFitData, MarkerSkippedMismatch, LapAnnotationsBackfillKey);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        for (var i = 0; i < deviceLaps.Count; i++)
        {
            deviceLaps[i].Intensity = kept[i].Intensity;
            deviceLaps[i].Trigger = kept[i].LapTrigger;
            deviceLaps[i].StepIndex = kept[i].StepIndex;
        }

        workout.RawFitData = StampMarker(workout.RawFitData, MarkerApplied, LapAnnotationsBackfillKey);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// True when RawFitData is a JSON object whose <c>lapsBackfill</c> is present
    /// and not JSON null. Parses the object so spaced Npgsql readback still counts as done.
    /// </summary>
    internal static bool IsLapsBackfillComplete(string? rawFitData)
    {
        if (string.IsNullOrEmpty(rawFitData))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawFitData);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return doc.RootElement.TryGetProperty(LapsBackfillKey, out var backfill) &&
                   backfill.ValueKind != JsonValueKind.Null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetPatchableObject(string rawFitData, out JsonObject? root)
    {
        try
        {
            root = JsonNode.Parse(rawFitData) as JsonObject;
            return root != null;
        }
        catch (JsonException)
        {
            root = null;
            return false;
        }
    }

    private async Task<List<Guid>> LoadCandidateIdsAsync(CancellationToken cancellationToken)
    {
        return await _db.Database
            .SqlQueryRaw<Guid>(PostgresCandidateSql)
            .ToListAsync(cancellationToken);
    }
}
