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
/// Also rewrites elapsed-based pace to moving when timer is still null.
/// Idempotent via column fill and top-level <c>timerTimeBackfill: "absent"</c>.
/// Clocks and pace only — no Track geometry Derive.
/// </summary>
public class TimerTimeBackfillService
{
    public const int BatchSize = 200;
    public const string TimerTimeAbsentMarker = "\"timerTimeBackfill\":\"absent\"";
    private const double PaceEpsilon = 1e-6;

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

        var changed = false;

        if (!workout.TimerTimeS.HasValue)
        {
            changed |= TryFillTimer(workout);
        }

        if (!workout.TimerTimeS.HasValue && workout.MovingTimeS.HasValue)
        {
            changed |= TryRewriteMovingPace(workout);
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    private bool TryFillTimer(Workout workout)
    {
        if (string.IsNullOrEmpty(workout.RawFitData))
        {
            return false;
        }

        if (workout.RawFitData.Contains(TimerTimeAbsentMarker, StringComparison.Ordinal))
        {
            return false;
        }

        var timerFromJson = TryReadTimerFromRawFitJson(workout.RawFitData);
        if (timerFromJson.HasValue)
        {
            workout.TimerTimeS = timerFromJson;
            WorkoutClocks.ApplyAvgPace(workout);
            return true;
        }

        if (workout.RawFileData != null && workout.RawFileData.Length > 0)
        {
            using var stream = new MemoryStream(workout.RawFileData);
            var isGzipped = workout.RawFileName?.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase) == true;
            var parseResult = isGzipped
                ? _fitParser.ParseGzippedFit(stream)
                : _fitParser.ParseFit(stream);

            var timerFromFile = TryReadTimerFromRawFitJson(parseResult.RawFitDataJson);
            if (timerFromFile.HasValue)
            {
                workout.TimerTimeS = timerFromFile;
                workout.RawFitData = PatchSessionTotalTimerTime(
                    workout.RawFitData,
                    parseResult.RawFitDataJson);
                WorkoutClocks.ApplyAvgPace(workout);
                return true;
            }

            workout.RawFitData = StampAbsent(workout.RawFitData);
            return true;
        }

        // JSON-only FIT with no usable totalTimerTime and no bytes.
        workout.RawFitData = StampAbsent(workout.RawFitData);
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

        root["timerTimeBackfill"] = "absent";
        return root.ToJsonString(JsonUtils.DefaultOptions);
    }

    private async Task<List<Guid>> LoadCandidateIdsAsync(CancellationToken cancellationToken)
    {
        var isPostgres = _db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        if (isPostgres)
        {
            // jsonb rejects text LIKE/Contains (22P02). Cast to text for the marker scan.
            return await _db.Database
                .SqlQueryRaw<Guid>(
                    """
                    SELECT w."Id" AS "Value"
                    FROM "Workouts" AS w
                    WHERE w."TimerTimeS" IS NULL
                      AND (
                        (
                          w."RawFitData" IS NOT NULL
                          AND w."RawFitData"::text <> ''
                          AND w."RawFitData"::text NOT LIKE {0}
                        )
                        OR w."MovingTimeS" IS NOT NULL
                      )
                    ORDER BY w."Id"
                    """,
                    "%" + TimerTimeAbsentMarker + "%")
                .ToListAsync(cancellationToken);
        }

        return await _db.Workouts
            .Where(w =>
                w.TimerTimeS == null &&
                (
                    (w.RawFitData != null &&
                     w.RawFitData != "" &&
                     !w.RawFitData.Contains(TimerTimeAbsentMarker)) ||
                    w.MovingTimeS != null
                ))
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
    }
}
