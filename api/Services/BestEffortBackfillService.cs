using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;

namespace Tempo.Api.Services;

/// <summary>
/// Fills <c>WorkoutBestEfforts</c> for workouts whose <c>BestEffortsComputedAt</c> is null
/// (created before per-workout best efforts, or restored from a Tempo ZIP). Replaces any rows the
/// workout already has. Stamps the marker even when the workout covers no standard distance, so each
/// workout is processed once. Leaves the all-time <c>BestEfforts</c> records as stored. Processes one
/// workout at a time so a long time series cannot OOM a batch.
/// </summary>
public class BestEffortBackfillService
{
    public const int BatchSize = 200;

    private readonly TempoDbContext _db;
    private readonly BestEffortService _bestEffortService;
    private readonly ILogger<BestEffortBackfillService> _logger;

    public BestEffortBackfillService(
        TempoDbContext db,
        BestEffortService bestEffortService,
        ILogger<BestEffortBackfillService> logger)
    {
        _db = db;
        _bestEffortService = bestEffortService;
        _logger = logger;
    }

    /// <summary>
    /// Computes per-workout best efforts for unmarked workouts. Returns the number of workouts marked.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var candidateIds = await _db.Workouts
            .Where(w => w.BestEffortsComputedAt == null)
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        var total = candidateIds.Count;
        if (total == 0)
        {
            _logger.LogInformation("Best effort backfill: {Processed} of {Total}", 0, 0);
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
                var workout = await _db.Workouts
                    .Include(w => w.Route)
                    .FirstOrDefaultAsync(w => w.Id == workoutId, cancellationToken);
                if (workout is null || workout.BestEffortsComputedAt != null)
                {
                    continue;
                }

                await _bestEffortService.ReplaceWorkoutBestEffortsAsync(_db, workout);

                processed++;
                if (processed % BatchSize == 0 || processed == total)
                {
                    _logger.LogInformation("Best effort backfill: {Processed} of {Total}", processed, total);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Best effort backfill failed for workout {WorkoutId}", workoutId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        if (processed > 0 && processed % BatchSize != 0 && processed != total)
        {
            _logger.LogInformation("Best effort backfill: {Processed} of {Total}", processed, total);
        }

        return processed;
    }
}
