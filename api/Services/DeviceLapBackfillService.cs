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

    /// <summary>Substring used to exclude marked FIT JSON from candidate scans.</summary>
    public const string LapsBackfillMarkerPrefix = "\"lapsBackfill\":";

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

        if (!string.IsNullOrEmpty(workout.RawFitData) &&
            workout.RawFitData.Contains(LapsBackfillMarkerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var hasFitJson = !string.IsNullOrEmpty(workout.RawFitData);
        var hasFitBytes = workout.RawFileData != null && workout.RawFileData.Length > 0 && LooksLikeFitFile(workout);
        if (!hasFitJson && !hasFitBytes)
        {
            return false;
        }

        // Ensure we have a JSON object to stamp / patch.
        if (!hasFitJson)
        {
            workout.RawFitData = "{}";
        }

        var sessionElapsed = TryReadSessionElapsedFromRawFitJson(workout.RawFitData);
        IReadOnlyList<DeviceLapSummary>? candidates = TryReadLapsFromRawFitJson(workout.RawFitData);
        var hadLapMessagesInJson = candidates != null;

        if ((!sessionElapsed.HasValue || candidates == null) && hasFitBytes)
        {
            using var stream = new MemoryStream(workout.RawFileData!);
            var isGzipped = workout.RawFileName?.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase) == true;
            var parseResult = isGzipped
                ? _fitParser.ParseGzippedFit(stream)
                : _fitParser.ParseFit(stream);

            sessionElapsed ??= TryReadSessionElapsedFromRawFitJson(parseResult.RawFitDataJson);
            if (candidates == null)
            {
                candidates = parseResult.Laps;
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
                LapTrigger = lap.TryGetProperty("trigger", out var trigger) &&
                             trigger.ValueKind == JsonValueKind.String
                    ? trigger.GetString()
                    : null
            });
        }

        return list;
    }

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

    private static string StampMarker(string existingJson, string value)
    {
        var root = JsonNode.Parse(existingJson)?.AsObject()
            ?? throw new InvalidOperationException("RawFitData is not a JSON object");

        root[LapsBackfillKey] = value;
        return root.ToJsonString(JsonUtils.DefaultOptions);
    }

    private async Task<List<Guid>> LoadCandidateIdsAsync(CancellationToken cancellationToken)
    {
        var markerPattern = "%" + LapsBackfillMarkerPrefix + "%";

        return await _db.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT w."Id" AS "Value"
                FROM "Workouts" AS w
                WHERE NOT EXISTS (
                    SELECT 1 FROM "WorkoutSplits" AS s
                    WHERE s."WorkoutId" = w."Id" AND s."Kind" = {0}
                )
                AND (
                  (
                    w."RawFitData" IS NOT NULL
                    AND w."RawFitData"::text <> ''
                    AND w."RawFitData"::text NOT LIKE {1}
                  )
                  OR (
                    w."RawFileData" IS NOT NULL
                    AND length(w."RawFileData") > 0
                    AND (
                      w."RawFitData" IS NULL
                      OR w."RawFitData"::text = ''
                      OR w."RawFitData"::text NOT LIKE {1}
                    )
                    AND (
                      lower(coalesce(w."RawFileType", '')) = 'fit'
                      OR lower(coalesce(w."RawFileName", '')) LIKE '%.fit'
                      OR lower(coalesce(w."RawFileName", '')) LIKE '%.fit.gz'
                    )
                  )
                )
                ORDER BY w."Id"
                """,
                WorkoutSplitKinds.DeviceLap,
                markerPattern)
            .ToListAsync(cancellationToken);
    }
}
