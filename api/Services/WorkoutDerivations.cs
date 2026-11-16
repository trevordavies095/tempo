using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// What changed on a Workout, so <see cref="WorkoutDerivations"/> knows which derived data is stale.
/// </summary>
public enum WorkoutChange
{
    /// <summary>Track points or time series were written or replaced (intake, duplicate upgrade, crop).</summary>
    Samples,

    /// <summary>Heart-rate zones changed in UserSettings, or a recalculation was requested.</summary>
    HeartRateZones
}

/// <param name="ZonesConfigured">False when no UserSettings row exists; heart-rate metrics were left as they were.</param>
/// <param name="RelativeEffort">The Workout's Relative Effort after recompute.</param>
public sealed record WorkoutDerivationsResult(bool ZonesConfigured, int? RelativeEffort);

/// <summary>
/// Brings a Workout's derived data in line after its inputs change: time in zones, Relative Effort,
/// and (for <see cref="WorkoutChange.Samples"/>) Best efforts. Track geometry and splits stay with the caller,
/// which already holds the track points. Saves the Workout.
/// </summary>
/// <remarks>
/// Relative Effort policy: without a UserSettings row the existing value is kept; with zones it is replaced,
/// which clears it when the Workout no longer has heart-rate data.
/// Best effort failures are logged and swallowed; heart-rate metric failures propagate.
/// </remarks>
public class WorkoutDerivations
{
    private readonly TempoDbContext _db;
    private readonly HeartRateZoneService _zoneService;
    private readonly IRelativeEffortService _relativeEffortService;
    private readonly IBestEffortService _bestEffortService;
    private readonly ILogger<WorkoutDerivations> _logger;

    public WorkoutDerivations(
        TempoDbContext db,
        HeartRateZoneService zoneService,
        IRelativeEffortService relativeEffortService,
        IBestEffortService bestEffortService,
        ILogger<WorkoutDerivations> logger)
    {
        _db = db;
        _zoneService = zoneService;
        _relativeEffortService = relativeEffortService;
        _bestEffortService = bestEffortService;
        _logger = logger;
    }

    public async Task<WorkoutDerivationsResult> RecomputeAsync(Workout workout, WorkoutChange change)
    {
        var settings = await _db.UserSettings.SingleRowAsync();
        if (settings != null)
        {
            var zones = _zoneService.GetZonesFromUserSettings(settings);
            workout.RelativeEffort = _relativeEffortService.RecalculateHeartRateMetrics(workout, zones, _db);
        }

        await _db.SaveChangesAsync();

        if (change == WorkoutChange.Samples)
        {
            try
            {
                await _bestEffortService.UpdateBestEffortsForNewWorkoutAsync(_db, workout);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update best efforts for workout {WorkoutId}", workout.Id);
            }
        }

        return new WorkoutDerivationsResult(settings != null, workout.RelativeEffort);
    }
}
