using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Fills <c>Workout.ZoneSecondsZ1..Z5</c> for workouts whose <c>ZoneSecondsComputedAt</c> is null
/// (created before stored zone seconds, or restored from a Tempo ZIP) using the current Settings zones.
/// Stamps the marker even when the workout has no HR series, so each workout is processed once.
/// Leaves Relative Effort as stored. When zones are not configured, nothing is marked and the next
/// startup retries. Processes one workout at a time so a long HR series cannot OOM a batch.
/// </summary>
public class ZoneSecondsBackfillService
{
    public const int BatchSize = 200;

    private readonly TempoDbContext _db;
    private readonly HeartRateZoneService _zoneService;
    private readonly IRelativeEffortService _relativeEffortService;
    private readonly ILogger<ZoneSecondsBackfillService> _logger;

    public ZoneSecondsBackfillService(
        TempoDbContext db,
        HeartRateZoneService zoneService,
        IRelativeEffortService relativeEffortService,
        ILogger<ZoneSecondsBackfillService> logger)
    {
        _db = db;
        _zoneService = zoneService;
        _relativeEffortService = relativeEffortService;
        _logger = logger;
    }

    /// <summary>
    /// Computes zone seconds for unmarked workouts. Returns the number of workouts marked.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var candidateIds = await _db.Workouts
            .Where(w => w.ZoneSecondsComputedAt == null)
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        var total = candidateIds.Count;
        if (total == 0)
        {
            _logger.LogInformation("Zone seconds backfill: {Processed} of {Total}", 0, 0);
            return 0;
        }

        var settings = await _db.UserSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (settings == null)
        {
            _logger.LogInformation(
                "Zone seconds backfill: skipped {Total} workouts; heart rate zones are not configured",
                total);
            return 0;
        }

        var zones = _zoneService.GetZonesFromUserSettings(settings);

        var processed = 0;
        foreach (var workoutId in candidateIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var workout = await _db.Workouts.FirstOrDefaultAsync(w => w.Id == workoutId, cancellationToken);
                if (workout is null || workout.ZoneSecondsComputedAt != null)
                {
                    continue;
                }

                // Relative Effort is returned, not assigned: the backfill only fills zone seconds.
                _relativeEffortService.RecalculateHeartRateMetrics(workout, zones, _db);
                if (workout.ZoneSecondsComputedAt == null)
                {
                    continue; // Invalid zones; retry next startup.
                }

                await _db.SaveChangesAsync(cancellationToken);

                processed++;
                if (processed % BatchSize == 0 || processed == total)
                {
                    _logger.LogInformation("Zone seconds backfill: {Processed} of {Total}", processed, total);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Zone seconds backfill failed for workout {WorkoutId}", workoutId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        if (processed > 0 && processed % BatchSize != 0 && processed != total)
        {
            _logger.LogInformation("Zone seconds backfill: {Processed} of {Total}", processed, total);
        }

        return processed;
    }
}
