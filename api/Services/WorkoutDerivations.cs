using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
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

/// <param name="ZonesConfigured">False when no heart-rate zones are saved; heart-rate metrics were left as they were.</param>
/// <param name="RelativeEffort">The Workout's Relative Effort after recompute.</param>
public sealed record WorkoutDerivationsResult(bool ZonesConfigured, int? RelativeEffort);

/// <param name="UpdatedCount">Workouts that ended with a Relative Effort value.</param>
/// <param name="Failures">Workouts whose recompute threw; their changes were reverted and not saved.</param>
public sealed record WorkoutDerivationsBatchResult(int UpdatedCount, IReadOnlyList<(Guid WorkoutId, Exception Error)> Failures);

/// <summary>
/// Brings a Workout's derived data in line after its inputs change: time in zones, Relative Effort,
/// and (for <see cref="WorkoutChange.Samples"/>) Best efforts. Track geometry and splits stay with the caller,
/// which already holds the track points. Saves the Workout.
/// </summary>
/// <remarks>
/// Relative Effort policy: without configured zones (see <see cref="HeartRateZoneService.HasConfiguredZones"/>)
/// the existing value is kept; with zones it is replaced, which clears it when the Workout no longer has
/// heart-rate data.
/// Best effort failures are logged and swallowed. A heart-rate metric failure propagates, but for
/// <see cref="WorkoutChange.Samples"/> only after best efforts have run, so one does not skip the other.
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
        var zonesConfigured = HeartRateZoneService.HasConfiguredZones(settings);

        ExceptionDispatchInfo? heartRateFailure = null;
        if (zonesConfigured)
        {
            try
            {
                ApplyHeartRateMetrics(workout, _zoneService.GetZonesFromUserSettings(settings!));
                await _db.SaveChangesAsync();
            }
            catch (Exception ex) when (change == WorkoutChange.Samples)
            {
                // Undo the unsaved metrics so the best-effort save below does not retry them.
                Revert(workout);
                heartRateFailure = ExceptionDispatchInfo.Capture(ex);
            }
        }

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

        heartRateFailure?.Throw();
        return new WorkoutDerivationsResult(zonesConfigured, workout.RelativeEffort);
    }

    /// <summary>
    /// Recomputes heart-rate metrics for many Workouts after a zone change, with one save at the end.
    /// A Workout whose recompute throws is reverted and reported in <see cref="WorkoutDerivationsBatchResult.Failures"/>;
    /// the rest are still saved. The caller has already checked <paramref name="settings"/> with
    /// <see cref="HeartRateZoneService.HasConfiguredZones"/>.
    /// </summary>
    public async Task<WorkoutDerivationsBatchResult> RecomputeHeartRateZonesAsync(
        IReadOnlyList<Workout> workouts,
        UserSettings settings)
    {
        var zones = _zoneService.GetZonesFromUserSettings(settings);
        var updatedCount = 0;
        var failures = new List<(Guid, Exception)>();

        foreach (var workout in workouts)
        {
            try
            {
                ApplyHeartRateMetrics(workout, zones);
                if (workout.RelativeEffort.HasValue)
                {
                    updatedCount++;
                }
            }
            catch (Exception ex)
            {
                Revert(workout);
                failures.Add((workout.Id, ex));
            }
        }

        await _db.SaveChangesAsync();
        return new WorkoutDerivationsBatchResult(updatedCount, failures);
    }

    private void ApplyHeartRateMetrics(Workout workout, List<HeartRateZone> zones)
    {
        workout.RelativeEffort = _relativeEffortService.RecalculateHeartRateMetrics(workout, zones, _db);
    }

    private void Revert(Workout workout)
    {
        var entry = _db.Entry(workout);
        if (entry.State == EntityState.Modified)
        {
            entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Unchanged;
        }
    }
}
