using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Stamps <c>WorkoutSplit.AvgHeartRateBpm</c> from existing WorkoutTimeSeries.
/// Does not re-derive km/mile geometry. Idempotent: workouts that already have
/// any filled split, or that have no heart-rate samples, are left alone.
/// </summary>
public class SplitHeartRateBackfillService
{
    public const int BatchSize = 200;

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
        foreach (var batchIds in candidateIds.Chunk(BatchSize))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var idBatch = batchIds.ToList();
            var workouts = await _db.Workouts
                .Where(w => idBatch.Contains(w.Id))
                .Include(w => w.Splits)
                .Include(w => w.TimeSeries)
                .ToListAsync(cancellationToken);

            foreach (var workout in workouts)
            {
                _splitHeartRate.ApplyToSplits(workout.Splits.ToList(), workout.TimeSeries.ToList());
            }

            await _db.SaveChangesAsync(cancellationToken);
            processed += workouts.Count;
            _logger.LogInformation(
                "Split heart rate backfill: {Processed} of {Total}",
                processed,
                total);
            _db.ChangeTracker.Clear();
        }

        return processed;
    }

    private IQueryable<Workout> CandidateQuery()
    {
        return _db.Workouts.Where(w =>
            _db.WorkoutSplits.Any(s => s.WorkoutId == w.Id) &&
            !_db.WorkoutSplits.Any(s => s.WorkoutId == w.Id && s.AvgHeartRateBpm != null) &&
            _db.WorkoutTimeSeries.Any(ts => ts.WorkoutId == w.Id && ts.HeartRateBpm != null));
    }
}
