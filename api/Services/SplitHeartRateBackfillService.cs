using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Stamps <c>WorkoutSplit.AvgHeartRateBpm</c> from existing WorkoutTimeSeries.
/// Does not re-derive km/mile geometry. Idempotent: workouts that already have
/// any filled split, no heart-rate samples, or a split-HR cursor are left alone.
/// No-overlap leftovers get <see cref="NoOverlapCursor"/> once; successful fills
/// drop out via any split BPM and do not write <c>applied</c>.
/// Processes one workout at a time so a long HR series cannot OOM or timeout
/// a 200-workout Include.
/// </summary>
public class SplitHeartRateBackfillService
{
    public const int BatchSize = 200;
    public const string NoOverlapCursor = "no_overlap";

    private readonly TempoDbContext _db;
    private readonly SplitHeartRateService _splitHeartRate;
    private readonly ILogger<SplitHeartRateBackfillService> _logger;

    public SplitHeartRateBackfillService(
        TempoDbContext db,
        SplitHeartRateService splitHeartRate,
        ILogger<SplitHeartRateBackfillService> logger)
    {
        _db = db;
        _splitHeartRate = splitHeartRate;
        _logger = logger;
    }

    /// <summary>
    /// Fills per-split average heart rate for candidate workouts.
    /// Returns the number of workouts updated.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var candidateIds = await CandidateQuery()
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        var total = candidateIds.Count;
        if (total == 0)
        {
            _logger.LogInformation("Split heart rate backfill: {Processed} of {Total}", 0, 0);
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
                if (!await FillWorkoutAsync(workoutId, cancellationToken))
                {
                    continue;
                }

                processed++;
                if (processed % BatchSize == 0 || processed == total)
                {
                    _logger.LogInformation(
                        "Split heart rate backfill: {Processed} of {Total}",
                        processed,
                        total);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Split heart rate backfill failed for workout {WorkoutId}", workoutId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        if (processed > 0 && processed % BatchSize != 0 && processed != total)
        {
            _logger.LogInformation("Split heart rate backfill: {Processed} of {Total}", processed, total);
        }

        return processed;
    }

    private async Task<bool> FillWorkoutAsync(Guid workoutId, CancellationToken cancellationToken)
    {
        var workout = await _db.Workouts
            .FirstOrDefaultAsync(w => w.Id == workoutId, cancellationToken);
        if (workout is null || workout.SplitHeartRateBackfill != null)
        {
            return false;
        }

        var splits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workoutId)
            .ToListAsync(cancellationToken);

        if (splits.Count == 0 || splits.Any(s => s.AvgHeartRateBpm != null))
        {
            return false;
        }

        var series = await _db.WorkoutTimeSeries
            .AsNoTracking()
            .Where(ts => ts.WorkoutId == workoutId)
            .Select(ts => new WorkoutTimeSeries
            {
                Id = ts.Id,
                ElapsedSeconds = ts.ElapsedSeconds,
                DistanceM = ts.DistanceM,
                HeartRateBpm = ts.HeartRateBpm
            })
            .ToListAsync(cancellationToken);

        var before = splits.ToDictionary(s => s.Id, s => s.AvgHeartRateBpm);
        _splitHeartRate.ApplyToSplits(splits, series);
        var bpmChanged = splits.Any(s => before[s.Id] != s.AvgHeartRateBpm);

        if (bpmChanged)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        workout.SplitHeartRateBackfill = NoOverlapCursor;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<Workout> CandidateQuery()
    {
        return _db.Workouts.Where(w =>
            w.SplitHeartRateBackfill == null &&
            _db.WorkoutSplits.Any(s => s.WorkoutId == w.Id) &&
            !_db.WorkoutSplits.Any(s => s.WorkoutId == w.Id && s.AvgHeartRateBpm != null) &&
            _db.WorkoutTimeSeries.Any(ts => ts.WorkoutId == w.Id && ts.HeartRateBpm != null));
    }
}
