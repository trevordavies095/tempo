using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Rewrites FIT cadence from strides/min to steps/min for workouts that still have
/// raw file bytes and unmarked RawFitData. Idempotent via <c>cadenceUnit</c> = <c>spm</c>
/// or a set <c>cadenceBackfill</c> (jsonb path on Postgres; JSON parse in-process;
/// compact and spaced Contains on SQLite). Cadence only — no Track geometry Derive.
/// </summary>
public class CadenceBackfillService
{
    public const int BatchSize = 200;
    public const string CadenceUnitSpmMarker = "\"cadenceUnit\":\"spm\"";
    public const string CadenceUnitSpmMarkerSpaced = "\"cadenceUnit\": \"spm\"";
    public const string CadenceBackfillKey = "cadenceBackfill";
    public const string CadenceBackfillUnparseable = "unparseable";
    public const string CadenceBackfillMarker = "\"cadenceBackfill\":";
    public const string CadenceBackfillMarkerSpaced = "\"cadenceBackfill\": ";
    private const string UnparseableStampJson = """{"cadenceBackfill":"unparseable"}""";

    /// <summary>
    /// Production candidate scan. jsonb path — do not LIKE compact <c>"cadenceUnit":"spm"</c>.
    /// </summary>
    public const string PostgresCandidateSql =
        """
        SELECT w."Id" AS "Value"
        FROM "Workouts" AS w
        WHERE w."RawFileData" IS NOT NULL
          AND w."RawFitData" IS NOT NULL
          AND w."RawFitData"::text <> ''
          AND (w."RawFitData"->>'cadenceUnit' IS DISTINCT FROM 'spm')
          AND w."RawFitData"->>'cadenceBackfill' IS NULL
        ORDER BY w."Id"
        """;

    private readonly TempoDbContext _db;
    private readonly FitParserService _fitParser;
    private readonly ILogger<CadenceBackfillService> _logger;

    public CadenceBackfillService(
        TempoDbContext db,
        FitParserService fitParser,
        ILogger<CadenceBackfillService> logger)
    {
        _db = db;
        _fitParser = fitParser;
        _logger = logger;
    }

    /// <summary>
    /// Re-parses unmarked FIT workouts and rewrites cadence fields.
    /// Returns the number of workouts updated.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var candidateIds = await LoadCandidateIdsAsync(cancellationToken);

        var total = candidateIds.Count;
        if (total == 0)
        {
            _logger.LogInformation("FIT cadence backfill: {Processed} of {Total}", 0, 0);
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
                            "FIT cadence backfill: {Processed} of {Total}",
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
                _logger.LogError(ex, "FIT cadence backfill failed for workout {WorkoutId}", workoutId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        if (processed > 0 && processed % BatchSize != 0 && processed != total)
        {
            _logger.LogInformation("FIT cadence backfill: {Processed} of {Total}", processed, total);
        }

        return processed;
    }

    private async Task<bool> FillWorkoutAsync(Guid workoutId, CancellationToken cancellationToken)
    {
        var workout = await _db.Workouts
            .FirstAsync(w => w.Id == workoutId, cancellationToken);

        if (workout.RawFileData == null || workout.RawFileData.Length == 0 ||
            string.IsNullOrEmpty(workout.RawFitData) ||
            IsCadenceBackfillComplete(workout.RawFitData))
        {
            return false;
        }

        if (!TryGetPatchableObject(workout.RawFitData, out _))
        {
            _logger.LogError(
                new InvalidOperationException("RawFitData is not a JSON object"),
                "FIT cadence backfill failed for workout {WorkoutId}",
                workoutId);
            workout.RawFitData = UnparseableStampJson;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        FitParserService.FitParseResult parseResult;
        try
        {
            using var stream = new MemoryStream(workout.RawFileData);
            var isGzipped = workout.RawFileName?.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase) == true;
            parseResult = isGzipped
                ? _fitParser.ParseGzippedFit(stream)
                : _fitParser.ParseFit(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "FIT cadence backfill failed for workout {WorkoutId}", workoutId);
            workout.RawFitData = StampExistingObject(workout.RawFitData);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var series = await _db.WorkoutTimeSeries
            .Where(ts => ts.WorkoutId == workoutId)
            .ToListAsync(cancellationToken);

        var cadenceByElapsed = BuildCadenceByElapsed(parseResult.SeriesPoints, workout.StartedAt);
        foreach (var row in series)
        {
            if (cadenceByElapsed.TryGetValue(row.ElapsedSeconds, out var cadence))
            {
                row.CadenceRpm = cadence;
            }
        }

        ApplyAggregates(workout, parseResult.RawFitDataJson, series);
        workout.RawFitData = PatchRawFitData(
            workout.RawFitData,
            parseResult.RawFitDataJson,
            parseResult.TrackPoints);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static Dictionary<int, byte> BuildCadenceByElapsed(
        IReadOnlyList<TrackPoint> seriesPoints,
        DateTime startedAt)
    {
        var map = new Dictionary<int, byte>();
        foreach (var point in seriesPoints)
        {
            if (!point.Time.HasValue || !point.CadenceRpm.HasValue)
            {
                continue;
            }

            var elapsed = (int)(point.Time.Value - startedAt).TotalSeconds;
            if (elapsed < 0)
            {
                continue;
            }

            map[elapsed] = point.CadenceRpm.Value;
        }

        return map;
    }

    private static void ApplyAggregates(
        Workout workout,
        string? parsedRawFitDataJson,
        List<WorkoutTimeSeries> series)
    {
        byte? avg = null;
        byte? max = null;

        if (!string.IsNullOrEmpty(parsedRawFitDataJson))
        {
            using var doc = JsonDocument.Parse(parsedRawFitDataJson);
            if (doc.RootElement.TryGetProperty("session", out var session) &&
                session.ValueKind == JsonValueKind.Object)
            {
                if (session.TryGetProperty("avgCadence", out var avgCad) &&
                    avgCad.ValueKind == JsonValueKind.Number)
                {
                    avg = (byte)avgCad.GetInt32();
                }

                if (session.TryGetProperty("maxCadence", out var maxCad) &&
                    maxCad.ValueKind == JsonValueKind.Number)
                {
                    max = (byte)maxCad.GetInt32();
                }
            }
        }

        if (!avg.HasValue || !max.HasValue)
        {
            var cadences = series
                .Where(ts => ts.CadenceRpm.HasValue)
                .Select(ts => ts.CadenceRpm!.Value)
                .ToList();
            if (cadences.Count > 0)
            {
                max ??= cadences.Max();
                avg ??= (byte)Math.Round(cadences.Average(x => (double)x));
            }
        }

        if (avg.HasValue)
        {
            workout.AvgCadenceRpm = avg;
        }

        if (max.HasValue)
        {
            workout.MaxCadenceRpm = max;
        }
    }

    private static string PatchRawFitData(
        string existingJson,
        string? parsedRawFitDataJson,
        List<TrackPoint> parsedTrackPoints)
    {
        var root = JsonNode.Parse(existingJson)?.AsObject()
            ?? throw new InvalidOperationException("RawFitData is not a JSON object");

        root["cadenceUnit"] = "spm";

        JsonObject? parsedSession = null;
        if (!string.IsNullOrEmpty(parsedRawFitDataJson))
        {
            var parsedRoot = JsonNode.Parse(parsedRawFitDataJson)?.AsObject();
            if (parsedRoot?["session"] is JsonObject session)
            {
                parsedSession = session;
            }
        }

        if (parsedSession != null)
        {
            var existingSession = root["session"] as JsonObject ?? new JsonObject();
            if (parsedSession["avgCadence"] != null)
            {
                existingSession["avgCadence"] = parsedSession["avgCadence"]!.DeepClone();
            }

            if (parsedSession["maxCadence"] != null)
            {
                existingSession["maxCadence"] = parsedSession["maxCadence"]!.DeepClone();
            }

            root["session"] = existingSession;
        }

        if (root["trackPoints"] is JsonArray existingPoints && existingPoints.Count > 0)
        {
            var cadenceByTime = new Dictionary<string, byte?>(StringComparer.Ordinal);
            foreach (var point in parsedTrackPoints)
            {
                if (point.Time.HasValue)
                {
                    cadenceByTime[point.Time.Value.ToString("O")] = point.CadenceRpm;
                }
            }

            for (var i = 0; i < existingPoints.Count; i++)
            {
                if (existingPoints[i] is not JsonObject pointObj)
                {
                    continue;
                }

                byte? cad = null;
                var time = pointObj["time"]?.GetValue<string>();
                if (time != null && cadenceByTime.TryGetValue(time, out var byTime))
                {
                    cad = byTime;
                }
                else if (i < parsedTrackPoints.Count)
                {
                    cad = parsedTrackPoints[i].CadenceRpm;
                }

                if (cad.HasValue)
                {
                    pointObj["cad"] = cad.Value;
                }
            }
        }

        return root.ToJsonString(JsonUtils.DefaultOptions);
    }

    /// <summary>
    /// True when RawFitData is a JSON object whose <c>cadenceUnit</c> is <c>spm</c>
    /// or whose <c>cadenceBackfill</c> is present and not JSON null.
    /// Parses the object so spaced Npgsql readback still counts as done.
    /// </summary>
    internal static bool IsCadenceBackfillComplete(string? rawFitData)
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

            if (doc.RootElement.TryGetProperty("cadenceUnit", out var unit) &&
                unit.ValueKind == JsonValueKind.String &&
                unit.GetString() == "spm")
            {
                return true;
            }

            return doc.RootElement.TryGetProperty(CadenceBackfillKey, out var backfill) &&
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
            root = JsonNode.Parse(rawFitData)?.AsObject();
            return root != null;
        }
        catch (JsonException)
        {
            root = null;
            return false;
        }
    }

    private static string StampExistingObject(string existingJson)
    {
        var root = JsonNode.Parse(existingJson)?.AsObject()
            ?? throw new InvalidOperationException("RawFitData is not a JSON object");
        root[CadenceBackfillKey] = CadenceBackfillUnparseable;
        return root.ToJsonString(JsonUtils.DefaultOptions);
    }

    private async Task<List<Guid>> LoadCandidateIdsAsync(CancellationToken cancellationToken)
    {
        // Length > 0 is checked in FillWorkoutAsync — EF cannot translate byte[].Length on SQLite.
        var isPostgres = _db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        if (isPostgres)
        {
            return await _db.Database
                .SqlQueryRaw<Guid>(PostgresCandidateSql)
                .ToListAsync(cancellationToken);
        }

        return await _db.Workouts
            .Where(w =>
                w.RawFileData != null &&
                w.RawFitData != null &&
                w.RawFitData != "" &&
                !w.RawFitData.Contains(CadenceUnitSpmMarker) &&
                !w.RawFitData.Contains(CadenceUnitSpmMarkerSpaced) &&
                !w.RawFitData.Contains(CadenceBackfillMarker) &&
                !w.RawFitData.Contains(CadenceBackfillMarkerSpaced))
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
    }
}
