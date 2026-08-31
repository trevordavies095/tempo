using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Recalculates workout splits based on unit preference.
/// </summary>
public class SplitRecalculationService
{
    private readonly TempoDbContext _db;
    private readonly TrackPointRehydration _rehydration;
    private readonly TrackGeometry _trackGeometry;
    private readonly ILogger<SplitRecalculationService> _logger;

    public SplitRecalculationService(
        TempoDbContext db,
        TrackPointRehydration rehydration,
        TrackGeometry trackGeometry,
        ILogger<SplitRecalculationService> logger)
    {
        _db = db;
        _rehydration = rehydration;
        _trackGeometry = trackGeometry;
        _logger = logger;
    }

    /// <summary>
    /// Recalculates splits for a single workout based on unit preference.
    /// </summary>
    public async Task<bool> RecalculateSplitsForWorkoutAsync(Workout workout, string unitPreference)
    {
        if (workout.Route == null)
        {
            _logger.LogWarning("Workout {WorkoutId} has no route data, skipping split recalculation", workout.Id);
            return false;
        }

        var splitDistanceMeters = unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase)
            ? 1609.344
            : 1000.0;

        var trackPoints = _rehydration.Rehydrate(workout);
        if (trackPoints == null || trackPoints.Count < 2)
        {
            _logger.LogWarning("Workout {WorkoutId} has insufficient track point data, skipping split recalculation", workout.Id);
            return false;
        }

        var existingSplits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workout.Id)
            .ToListAsync();

        if (existingSplits.Count > 0)
        {
            _db.WorkoutSplits.RemoveRange(existingSplits);
        }

        var derived = _trackGeometry.Derive(
            trackPoints,
            workout.StartedAt,
            splitDistanceMeters,
            workout.Id,
            workout.DistanceM,
            workout.DurationS);

        if (derived.HasRouteCoordinates && workout.Route != null)
        {
            workout.Route.RouteGeoJson = derived.Route.RouteGeoJson;
            workout.Route.PreviewGeoJson = derived.Route.PreviewGeoJson;
        }

        _db.WorkoutSplits.AddRange(derived.Splits);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Recalculated splits for workout {WorkoutId}: {OldCount} -> {NewCount} splits",
            workout.Id, existingSplits.Count, derived.Splits.Count);

        return true;
    }

    /// <summary>
    /// Recalculates splits for all workouts that have route data.
    /// </summary>
    public async Task<SplitRecalculationResult> RecalculateSplitsForAllWorkoutsAsync(string unitPreference)
    {
        var workouts = await _db.Workouts
            .Include(w => w.Route)
            .Where(w => w.Route != null)
            .Select(w => new Workout
            {
                Id = w.Id,
                StartedAt = w.StartedAt,
                DistanceM = w.DistanceM,
                DurationS = w.DurationS,
                Source = w.Source,
                RawGpxData = w.RawGpxData,
                RawFitData = w.RawFitData,
                RawFileData = w.RawFileData,
                RawFileName = w.RawFileName,
                RawFileType = w.RawFileType,
                Route = w.Route
            })
            .ToListAsync();

        int successCount = 0;
        int errorCount = 0;
        var errors = new List<string>();

        foreach (var workout in workouts)
        {
            try
            {
                var success = await RecalculateSplitsForWorkoutAsync(workout, unitPreference);
                if (success)
                {
                    successCount++;
                }
                else
                {
                    errorCount++;
                    errors.Add($"Workout {workout.Id}: Insufficient data for split recalculation");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "Error recalculating splits for workout {WorkoutId}", workout.Id);
                errors.Add($"Workout {workout.Id}: {ex.Message}");
            }
        }

        return new SplitRecalculationResult
        {
            TotalWorkouts = workouts.Count,
            SuccessCount = successCount,
            ErrorCount = errorCount,
            Errors = errors
        };
    }

    /// <summary>
    /// Result of recalculating splits for all workouts.
    /// </summary>
    public class SplitRecalculationResult
    {
        public int TotalWorkouts { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
