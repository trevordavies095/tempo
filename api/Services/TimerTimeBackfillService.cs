using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Backfills <see cref="Workout.TimerTimeS"/> from FIT session JSON (else reparses
/// <see cref="Workout.RawFileData"/>) and rewrites <see cref="Workout.AvgPaceS"/>.
/// Moving-time pace rewrite is only a side effect while a FIT-JSON leftover is filled.
/// Idempotent via column fill and <c>timerTimeBackfill</c> (jsonb path on Postgres;
/// JSON parse in-process). Clocks and pace only — no Track geometry Derive.
/// </summary>
public class TimerTimeBackfillService
{
    public const int BatchSize = 200;
    public const string TimerTimeBackfillKey = "timerTimeBackfill";
    public const string TimerTimeBackfillAbsent = "absent";
    public const string TimerTimeBackfillUnparseable = "unparseable";
    public const string TimerTimeAbsentMarker = "\"timerTimeBackfill\":\"absent\"";
    private const string UnparseableStampJson = """{"timerTimeBackfill":"unparseable"}""";
    private const double PaceEpsilon = 1e-6;

    /// <summary>
    /// Production candidate scan. jsonb path — do not LIKE compact
    /// <c>"timerTimeBackfill":"absent"</c>.
    /// </summary>
    public const string PostgresCandidateSql =
        """
        SELECT w."Id" AS "Value"
        FROM "Workouts" AS w
        WHERE w."TimerTimeS" IS NULL
          AND w."RawFitData" IS NOT NULL
          AND w."RawFitData"::text <> ''
          AND w."RawFitData"->>'timerTimeBackfill' IS NULL
        ORDER BY w."Id"
        """;

    private readonly TempoDbContext _db;
    private readonly FitParserService _fitParser;
    private readonly ILogger<TimerTimeBackfillService> _logger;

    public TimerTimeBackfillService(
        TempoDbContext db,
        FitParserService fitParser,
        ILogger<TimerTimeBackfillService> logger)
    {
        _db = db;
        _fitParser = fitParser;
        _logger = logger;
    }

    /// <summary>
    /// Fills timer time and pace for candidate workouts.
    /// Returns the number of workouts updated.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var candidateIds = await LoadCandidateIdsAsync(cancellationToken);

        var total = candidateIds.Count;
        if (total == 0)
        {
            _logger.LogInformation("Timer time backfill: {Processed} of {Total}", 0, 0);
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
                            "Timer time backfill: {Processed} of {Total}",
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
                _logger.LogError(ex, "Timer time backfill failed for workout {WorkoutId}", workoutId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        if (processed > 0 && processed % BatchSize != 0 && processed != total)
        {
            _logger.LogInformation("Timer time backfill: {Processed} of {Total}", processed, total);
        }

        return processed;
    }

    private async Task<bool> FillWorkoutAsync(Guid workoutId, CancellationToken cancellationToken)
    {
        var workout = await _db.Workouts
            .FirstAsync(w => w.Id == workoutId, cancellationToken);

        if (workout.TimerTimeS.HasValue ||
            string.IsNullOrEmpty(workout.RawFitData) ||
            IsTimerTimeBackfillComplete(workout.RawFitData))
        {
            return false;
        }

        if (!TryGetPatchableObject(workout.RawFitData, out _))
        {
            _logger.LogError(
                new InvalidOperationException("RawFitData is not a JSON object"),
                "Timer time backfill failed for workout {WorkoutId}",
                workoutId);
            workout.RawFitData = UnparseableStampJson;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var timerFromJson = TryReadTimerFromRawFitJson(workout.RawFitData);
        if (timerFromJson.HasValue)
        {
            workout.TimerTimeS = timerFromJson;
            WorkoutClocks.ApplyAvgPace(workout);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (workout.RawFileData != null && workout.RawFileData.Length > 0)
        {
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
                _logger.LogError(ex, "Timer time backfill failed for workout {WorkoutId}", workoutId);
                workout.RawFitData = StampExistingObject(workout.RawFitData);
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }

            var timerFromFile = TryReadTimerFromRawFitJson(parseResult.RawFitDataJson);
            if (timerFromFile.HasValue)
            {
                workout.TimerTimeS = timerFromFile;
                workout.RawFitData = PatchSessionTotalTimerTime(
                    workout.RawFitData,
                    parseResult.RawFitDataJson);
                WorkoutClocks.ApplyAvgPace(workout);
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
        }

        workout.RawFitData = StampAbsent(workout.RawFitData);
        if (!workout.TimerTimeS.HasValue && workout.MovingTimeS.HasValue)
        {
            TryRewriteMovingPace(workout);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool TryRewriteMovingPace(Workout workout)
    {
        var expected = ExpectedPaceFromMoving(workout);
        if (expected.HasValue && Math.Abs(workout.AvgPaceS - expected.Value) <= PaceEpsilon)
        {
            return false;
        }

        WorkoutClocks.ApplyAvgPace(workout);
        return true;
    }

    private static double? ExpectedPaceFromMoving(Workout workout)
    {
        if (!workout.MovingTimeS.HasValue || workout.MovingTimeS.Value <= 0 || workout.DistanceM <= 0)
        {
            return null;
        }

        return workout.MovingTimeS.Value / (workout.DistanceM / 1000.0);
    }

    private static int? TryReadTimerFromRawFitJson(string? rawFitData)
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

        if (!session.TryGetProperty("totalTimerTime", out var timerTime))
        {
            return null;
        }

        return WorkoutClocks.SecondsFromJsonNumber(timerTime);
    }

    private static string PatchSessionTotalTimerTime(string existingJson, string? parsedRawFitDataJson)
    {
        var root = JsonNode.Parse(existingJson)?.AsObject()
            ?? throw new InvalidOperationException("RawFitData is not a JSON object");

        JsonNode? totalTimerTime = null;
        if (!string.IsNullOrEmpty(parsedRawFitDataJson))
        {
            var parsedRoot = JsonNode.Parse(parsedRawFitDataJson)?.AsObject();
            if (parsedRoot?["session"] is JsonObject parsedSession)
            {
                totalTimerTime = parsedSession["totalTimerTime"];
            }
        }

        if (totalTimerTime != null)
        {
            var existingSession = root["session"] as JsonObject ?? new JsonObject();
            existingSession["totalTimerTime"] = totalTimerTime.DeepClone();
            root["session"] = existingSession;
        }

        return root.ToJsonString(JsonUtils.DefaultOptions);
    }

    private static string StampAbsent(string existingJson)
    {
        var root = JsonNode.Parse(existingJson)?.AsObject()
            ?? throw new InvalidOperationException("RawFitData is not a JSON object");

        root[TimerTimeBackfillKey] = TimerTimeBackfillAbsent;
        return root.ToJsonString(JsonUtils.DefaultOptions);
    }

    /// <summary>
    /// True when RawFitData is a JSON object whose <c>timerTimeBackfill</c> is
    /// present and not JSON null. Parses the object so spaced Npgsql readback
    /// still counts as done.
    /// </summary>
    internal static bool IsTimerTimeBackfillComplete(string? rawFitData)
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

            return doc.RootElement.TryGetProperty(TimerTimeBackfillKey, out var backfill) &&
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

    private static string StampExistingObject(string existingJson)
    {
        var root = JsonNode.Parse(existingJson)?.AsObject()
            ?? throw new InvalidOperationException("RawFitData is not a JSON object");
        root[TimerTimeBackfillKey] = TimerTimeBackfillUnparseable;
        return root.ToJsonString(JsonUtils.DefaultOptions);
    }

    private async Task<List<Guid>> LoadCandidateIdsAsync(CancellationToken cancellationToken)
    {
        return await _db.Database
            .SqlQueryRaw<Guid>(PostgresCandidateSql)
            .ToListAsync(cancellationToken);
    }
}
