using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Service for calculating and managing best effort times for standard running distances.
/// Best efforts are calculated from any segment within any workout.
/// </summary>
public class BestEffortService : IBestEffortService
{
    private readonly ILogger<BestEffortService> _logger;

    /// <summary>
    /// Standard running distances in meters
    /// </summary>
    public static readonly Dictionary<string, double> StandardDistances = new()
    {
        { "400m", 400 },
        { "1/2 mile", 804.672 },
        { "1K", 1000 },
        { "1 mile", 1609.344 },
        { "2 mile", 3218.688 },
        { "5K", 5000 },
        { "10K", 10000 },
        { "15K", 15000 },
        { "10 mile", 16093.44 },
        { "20K", 20000 },
        { "Half-Marathon", 21097.5 },
        { "30K", 30000 },
        { "Marathon", 42195 }
    };

    public BestEffortService(ILogger<BestEffortService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Fastest per-workout best effort for each distance, optionally limited to workouts started within
    /// [<paramref name="from"/>, <paramref name="to"/>] (inclusive UTC dates). Ties go to the earliest workout.
    /// Distances with no effort in range are omitted.
    /// </summary>
    public async Task<List<BestEffortResult>> GetBestEffortsAsync(
        TempoDbContext db,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var efforts = db.WorkoutBestEfforts.AsNoTracking().AsQueryable();
        if (from is { } fromDate)
        {
            var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            efforts = efforts.Where(wbe => wbe.Workout.StartedAt >= fromUtc);
        }
        if (to is { } toDate)
        {
            var toExclusiveUtc = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            efforts = efforts.Where(wbe => wbe.Workout.StartedAt < toExclusiveUtc);
        }

        var bestEfforts = await efforts
            .GroupBy(wbe => wbe.Distance)
            .Select(g => g
                .OrderBy(wbe => wbe.TimeS)
                .ThenBy(wbe => wbe.Workout.StartedAt)
                .Select(wbe => new
                {
                    wbe.Distance,
                    wbe.DistanceM,
                    wbe.TimeS,
                    wbe.WorkoutId,
                    wbe.Workout.StartedAt
                })
                .First())
            .ToListAsync();

        return bestEfforts
            .OrderBy(be => be.DistanceM)
            .Select(be => new BestEffortResult
            {
                Distance = be.Distance,
                DistanceM = be.DistanceM,
                TimeS = be.TimeS,
                WorkoutId = be.WorkoutId.ToString(),
                WorkoutDate = be.StartedAt.ToString("yyyy-MM-dd")
            })
            .ToList();
    }

    /// <summary>
    /// Calculate best effort for a specific workout and target distance.
    /// Returns the fastest time (in seconds) for the target distance, or null if not found.
    /// </summary>
    public async Task<BestEffortResult?> CalculateBestEffortForWorkoutAsync(
        TempoDbContext db,
        Workout workout,
        string distanceName,
        double targetDistanceM)
    {
        if (workout.DistanceM < targetDistanceM)
        {
            return null; // Workout is too short
        }

        var timeSeries = await LoadDistanceTimeSeriesAsync(db, workout.Id);
        return CalculateBestEffortForWorkout(workout, timeSeries, distanceName, targetDistanceM);
    }

    private static Task<List<WorkoutTimeSeries>> LoadDistanceTimeSeriesAsync(TempoDbContext db, Guid workoutId)
    {
        return db.WorkoutTimeSeries
            .AsNoTracking()
            .Where(ts => ts.WorkoutId == workoutId && ts.DistanceM.HasValue)
            .OrderBy(ts => ts.ElapsedSeconds)
            .ToListAsync();
    }

    /// <summary>
    /// Calculate best effort for a workout and target distance from an already-loaded time series,
    /// so callers computing every distance load the series once.
    /// </summary>
    private BestEffortResult? CalculateBestEffortForWorkout(
        Workout workout,
        List<WorkoutTimeSeries> timeSeries,
        string distanceName,
        double targetDistanceM)
    {
        if (workout.DistanceM < targetDistanceM)
        {
            return null; // Workout is too short
        }

        // Try time series method first (most accurate)
        int? bestTimeS = null;

        if (timeSeries.Count > 0)
        {
            bestTimeS = CalculateBestEffortFromTimeSeries(timeSeries, targetDistanceM);
        }

        // Fallback to route-based calculation if time series unavailable or insufficient
        if (!bestTimeS.HasValue && workout.Route != null)
        {
            bestTimeS = CalculateBestEffortFromRoute(workout, targetDistanceM);
        }

        if (!bestTimeS.HasValue)
        {
            return null;
        }

        return new BestEffortResult
        {
            Distance = distanceName,
            DistanceM = targetDistanceM,
            TimeS = bestTimeS.Value,
            WorkoutId = workout.Id.ToString(),
            WorkoutDate = workout.StartedAt.ToString("yyyy-MM-dd")
        };
    }

    /// <summary>
    /// Calculate best effort from time series data using rolling window.
    /// </summary>
    private int? CalculateBestEffortFromTimeSeries(List<WorkoutTimeSeries> timeSeries, double targetDistanceM)
    {
        if (timeSeries.Count < 2)
        {
            return null;
        }

        int? bestTimeS = null;
        int startIndex = 0;

        for (int endIndex = 1; endIndex < timeSeries.Count; endIndex++)
        {
            var startPoint = timeSeries[startIndex];
            var endPoint = timeSeries[endIndex];

            // If start point has no distance, advance startIndex and skip this iteration
            if (!startPoint.DistanceM.HasValue)
            {
                // Only advance if we haven't caught up to endIndex
                if (startIndex < endIndex - 1)
                {
                    startIndex++;
                }
                continue;
            }

            // If end point has no distance, skip this iteration (but don't advance startIndex)
            if (!endPoint.DistanceM.HasValue)
            {
                continue;
            }

            var segmentDistance = endPoint.DistanceM.Value - startPoint.DistanceM.Value;

            // If we've reached or exceeded the target distance, calculate time
            if (segmentDistance >= targetDistanceM)
            {
                var timeS = endPoint.ElapsedSeconds - startPoint.ElapsedSeconds;
                if (timeS > 0 && (!bestTimeS.HasValue || timeS < bestTimeS.Value))
                {
                    bestTimeS = timeS;
                }

                // Move start index forward to find next segment
                // Try to find the closest point that gives us exactly targetDistanceM
                while (startIndex < endIndex - 1)
                {
                    var nextStartPoint = timeSeries[startIndex + 1];
                    if (!nextStartPoint.DistanceM.HasValue)
                    {
                        startIndex++;
                        continue;
                    }

                    var newSegmentDistance = endPoint.DistanceM.Value - nextStartPoint.DistanceM.Value;
                    if (newSegmentDistance >= targetDistanceM)
                    {
                        startIndex++;
                        var newTimeS = endPoint.ElapsedSeconds - nextStartPoint.ElapsedSeconds;
                        if (newTimeS > 0 && (!bestTimeS.HasValue || newTimeS < bestTimeS.Value))
                        {
                            bestTimeS = newTimeS;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        return bestTimeS;
    }

    /// <summary>
    /// Calculate best effort from route coordinates (fallback when time series unavailable).
    /// Estimates timestamps based on workout duration and distance ratio.
    /// </summary>
    private int? CalculateBestEffortFromRoute(Workout workout, double targetDistanceM)
    {
        if (workout.Route == null || string.IsNullOrEmpty(workout.Route.RouteGeoJson))
        {
            return null;
        }

        try
        {
            var geoJson = JsonSerializer.Deserialize<JsonElement>(workout.Route.RouteGeoJson);
            
            // Handle case where deserialization results in null JsonElement (e.g., from literal "null" string)
            // This can happen with corrupted data from earlier imports
            if (geoJson.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            
            if (!geoJson.TryGetProperty("coordinates", out var coordinatesElement))
            {
                return null;
            }

            var coordinates = coordinatesElement.EnumerateArray().ToList();
            if (coordinates.Count < 2)
            {
                return null;
            }

            // Calculate cumulative distances
            var cumulativeDistances = new List<double> { 0.0 };
            double totalDistance = 0.0;

            for (int i = 1; i < coordinates.Count; i++)
            {
                var prevCoord = coordinates[i - 1].EnumerateArray().ToArray();
                var currCoord = coordinates[i].EnumerateArray().ToArray();

                if (prevCoord.Length >= 2 && currCoord.Length >= 2)
                {
                    var segmentDistance = GeoUtils.HaversineDistance(
                        prevCoord[1].GetDouble(), // latitude
                        prevCoord[0].GetDouble(), // longitude
                        currCoord[1].GetDouble(),
                        currCoord[0].GetDouble()
                    );
                    totalDistance += segmentDistance;
                    cumulativeDistances.Add(totalDistance);
                }
                else
                {
                    cumulativeDistances.Add(totalDistance); // Use previous distance if invalid
                }
            }

            if (totalDistance < targetDistanceM)
            {
                return null; // Route is too short
            }

            // Estimate timestamps: linear interpolation based on distance ratio
            var estimatedTimestamps = new List<int>();
            for (int i = 0; i < cumulativeDistances.Count; i++)
            {
                var distanceRatio = totalDistance > 0 ? cumulativeDistances[i] / totalDistance : 0;
                var estimatedSeconds = (int)(workout.DurationS * distanceRatio);
                estimatedTimestamps.Add(estimatedSeconds);
            }

            // Find best effort using rolling window (similar to time series method)
            int? bestTimeS = null;
            int startIndex = 0;

            for (int endIndex = 1; endIndex < cumulativeDistances.Count; endIndex++)
            {
                var segmentDistance = cumulativeDistances[endIndex] - cumulativeDistances[startIndex];

                if (segmentDistance >= targetDistanceM)
                {
                    var timeS = estimatedTimestamps[endIndex] - estimatedTimestamps[startIndex];
                    if (timeS > 0 && (!bestTimeS.HasValue || timeS < bestTimeS.Value))
                    {
                        bestTimeS = timeS;
                    }

                    // Move start index forward
                    while (startIndex < endIndex - 1)
                    {
                        var newSegmentDistance = cumulativeDistances[endIndex] - cumulativeDistances[startIndex + 1];
                        if (newSegmentDistance >= targetDistanceM)
                        {
                            startIndex++;
                            var newTimeS = estimatedTimestamps[endIndex] - estimatedTimestamps[startIndex];
                            if (newTimeS > 0 && (!bestTimeS.HasValue || newTimeS < bestTimeS.Value))
                            {
                                bestTimeS = newTimeS;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            return bestTimeS;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate best effort from route for workout {WorkoutId}", workout.Id);
            return null;
        }
    }

    /// <summary>
    /// Recompute a workout's <see cref="WorkoutBestEffort"/> rows: one per standard distance it covers,
    /// replacing any it already has, and stamp <see cref="Workout.BestEffortsComputedAt"/>.
    /// The workout must be tracked with its Route loaded (route fallback). Saves changes.
    /// </summary>
    public async Task<List<BestEffortResult>> ReplaceWorkoutBestEffortsAsync(TempoDbContext db, Workout workout)
    {
        var timeSeries = await LoadDistanceTimeSeriesAsync(db, workout.Id);

        var results = new List<BestEffortResult>();
        foreach (var (distanceName, targetDistanceM) in StandardDistances)
        {
            var result = CalculateBestEffortForWorkout(workout, timeSeries, distanceName, targetDistanceM);
            if (result != null)
            {
                results.Add(result);
            }
        }

        var existingRows = await db.WorkoutBestEfforts
            .Where(wbe => wbe.WorkoutId == workout.Id)
            .ToListAsync();
        db.WorkoutBestEfforts.RemoveRange(existingRows);

        db.WorkoutBestEfforts.AddRange(results.Select(r => new WorkoutBestEffort
        {
            WorkoutId = workout.Id,
            Distance = r.Distance,
            DistanceM = r.DistanceM,
            TimeS = r.TimeS
        }));

        workout.BestEffortsComputedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogDebug("Stored {Count} workout best efforts for workout {WorkoutId}", results.Count, workout.Id);

        return results;
    }

    /// <summary>
    /// Rebuild every workout's <see cref="WorkoutBestEffort"/> rows (full recalculation) and return
    /// the resulting all-time best efforts.
    /// </summary>
    public async Task<List<BestEffortResult>> CalculateAllBestEffortsAsync(TempoDbContext db)
    {
        _logger.LogInformation("Starting full recalculation of best efforts");

        await db.WorkoutBestEfforts.ExecuteDeleteAsync();

        var workouts = await db.Workouts
            .Include(w => w.Route)
            .ToListAsync();

        foreach (var workout in workouts)
        {
            await ReplaceWorkoutBestEffortsAsync(db, workout);
        }

        var results = await GetBestEffortsAsync(db);

        _logger.LogInformation("Completed recalculation of best efforts. Found {Count} best efforts", results.Count);

        return results;
    }

    /// <summary>
    /// Compute and store best efforts for a new or changed (e.g. cropped) workout.
    /// </summary>
    public async Task UpdateBestEffortsForNewWorkoutAsync(TempoDbContext db, Workout workout)
    {
        _logger.LogDebug("Updating best efforts for workout {WorkoutId} (distance: {DistanceM}m)", workout.Id, workout.DistanceM);

        // Load workout with route
        var workoutWithRoute = await db.Workouts
            .Include(w => w.Route)
            .FirstOrDefaultAsync(w => w.Id == workout.Id);

        if (workoutWithRoute == null)
        {
            _logger.LogWarning("Workout {WorkoutId} not found when updating best efforts", workout.Id);
            return;
        }

        await ReplaceWorkoutBestEffortsAsync(db, workoutWithRoute);
    }

    /// <summary>
    /// Result object for best effort calculations.
    /// </summary>
    public class BestEffortResult
    {
        public string Distance { get; set; } = string.Empty;
        public double DistanceM { get; set; }
        public int TimeS { get; set; }
        public string WorkoutId { get; set; } = string.Empty;
        public string WorkoutDate { get; set; } = string.Empty;
    }
}

