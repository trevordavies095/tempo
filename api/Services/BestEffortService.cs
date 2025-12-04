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
public class BestEffortService
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
    /// Get all stored best efforts from the database.
    /// </summary>
    public async Task<List<BestEffortResult>> GetBestEffortsAsync(TempoDbContext db)
    {
        var bestEfforts = await db.BestEfforts
            .OrderBy(be => be.DistanceM)
            .ToListAsync();

        return bestEfforts.Select(be => new BestEffortResult
        {
            Distance = be.Distance,
            DistanceM = be.DistanceM,
            TimeS = be.TimeS,
            WorkoutId = be.WorkoutId.ToString(),
            WorkoutDate = be.WorkoutDate.ToString("yyyy-MM-dd")
        }).ToList();
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

        // Try time series method first (most accurate)
        var timeSeries = await db.WorkoutTimeSeries
            .Where(ts => ts.WorkoutId == workout.Id && ts.DistanceM.HasValue)
            .OrderBy(ts => ts.ElapsedSeconds)
            .ToListAsync();

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
    /// Calculate all best efforts across all workouts (full recalculation).
    /// </summary>
    public async Task<List<BestEffortResult>> CalculateAllBestEffortsAsync(TempoDbContext db)
    {
        _logger.LogInformation("Starting full recalculation of best efforts");

        // Clear existing best efforts
        await ClearBestEffortsAsync(db);

        var results = new Dictionary<string, BestEffortResult>();

        // Get all workouts ordered by distance (descending) to process longer workouts first
        var workouts = await db.Workouts
            .Include(w => w.Route)
            .OrderByDescending(w => w.DistanceM)
            .ToListAsync();

        foreach (var distanceEntry in StandardDistances.OrderByDescending(d => d.Value))
        {
            var distanceName = distanceEntry.Key;
            var targetDistanceM = distanceEntry.Value;

            _logger.LogDebug("Calculating best effort for {Distance} ({DistanceM}m)", distanceName, targetDistanceM);

            // Only process workouts that are long enough
            var qualifyingWorkouts = workouts.Where(w => w.DistanceM >= targetDistanceM).ToList();

            foreach (var workout in qualifyingWorkouts)
            {
                var result = await CalculateBestEffortForWorkoutAsync(db, workout, distanceName, targetDistanceM);
                if (result != null)
                {
                    // Update if this is faster than current best
                    if (!results.ContainsKey(distanceName) || result.TimeS < results[distanceName].TimeS)
                    {
                        results[distanceName] = result;
                    }
                }
            }
        }

        // Save all best efforts to database
        var bestEffortsToSave = results.Values.Select(r => new BestEffort
        {
            Distance = r.Distance,
            DistanceM = r.DistanceM,
            TimeS = r.TimeS,
            WorkoutId = Guid.Parse(r.WorkoutId),
            WorkoutDate = DateTime.SpecifyKind(DateTime.Parse(r.WorkoutDate), DateTimeKind.Utc),
            CalculatedAt = DateTime.UtcNow
        }).ToList();

        db.BestEfforts.AddRange(bestEffortsToSave);
        await db.SaveChangesAsync();

        _logger.LogInformation("Completed recalculation of best efforts. Found {Count} best efforts", bestEffortsToSave.Count);

        return results.Values.OrderBy(r => r.DistanceM).ToList();
    }

    /// <summary>
    /// Update best efforts incrementally for a new workout.
    /// Only checks distances where the workout is long enough and updates if faster.
    /// </summary>
    public async Task UpdateBestEffortsForNewWorkoutAsync(TempoDbContext db, Workout workout)
    {
        _logger.LogDebug("Updating best efforts for new workout {WorkoutId} (distance: {DistanceM}m)", workout.Id, workout.DistanceM);

        // Load workout with route
        var workoutWithRoute = await db.Workouts
            .Include(w => w.Route)
            .FirstOrDefaultAsync(w => w.Id == workout.Id);

        if (workoutWithRoute == null)
        {
            _logger.LogWarning("Workout {WorkoutId} not found when updating best efforts", workout.Id);
            return;
        }

        // Get all existing best efforts BEFORE making any changes to ensure we preserve them
        // Use AsNoTracking() to avoid change tracking issues, then query fresh when needed
        var allExistingBestEfforts = await db.BestEfforts
            .AsNoTracking()
            .Select(be => be.Distance)
            .ToListAsync();

        _logger.LogDebug("Found {Count} existing best efforts before update: {Distances}", 
            allExistingBestEfforts.Count, 
            string.Join(", ", allExistingBestEfforts));

        var createdCount = 0;
        var updatedCount = 0;
        var preservedCount = 0;
        var processedDistances = new List<string>();

        foreach (var distanceEntry in StandardDistances)
        {
            var distanceName = distanceEntry.Key;
            var targetDistanceM = distanceEntry.Value;

            // Skip if workout is too short
            if (workoutWithRoute.DistanceM < targetDistanceM)
            {
                _logger.LogDebug("Skipping {Distance} ({DistanceM}m) - workout too short ({WorkoutDistanceM}m)", 
                    distanceName, targetDistanceM, workoutWithRoute.DistanceM);
                continue;
            }

            processedDistances.Add(distanceName);

            // Calculate best effort for this distance from the new workout
            var newBestEffort = await CalculateBestEffortForWorkoutAsync(db, workoutWithRoute, distanceName, targetDistanceM);
            if (newBestEffort == null)
            {
                _logger.LogDebug("Could not calculate best effort for {Distance} from workout {WorkoutId}", 
                    distanceName, workoutWithRoute.Id);
                continue;
            }

            // Query for existing best effort using AsNoTracking first to check existence,
            // then load with tracking only if we need to update it
            var existingBestEffort = await db.BestEfforts
                .FirstOrDefaultAsync(be => be.Distance == distanceName);

            if (existingBestEffort == null)
            {
                // No existing best effort, create new one
                // Ensure StartedAt is UTC (defensive check)
                var workoutDate = workoutWithRoute.StartedAt.Kind == DateTimeKind.Utc
                    ? workoutWithRoute.StartedAt
                    : DateTime.SpecifyKind(workoutWithRoute.StartedAt, DateTimeKind.Utc);
                
                var bestEffort = new BestEffort
                {
                    Distance = distanceName,
                    DistanceM = targetDistanceM,
                    TimeS = newBestEffort.TimeS,
                    WorkoutId = workoutWithRoute.Id,
                    WorkoutDate = workoutDate,
                    CalculatedAt = DateTime.UtcNow
                };
                db.BestEfforts.Add(bestEffort);
                createdCount++;
                _logger.LogDebug("Created new best effort for {Distance}: {TimeS}s from workout {WorkoutId}", 
                    distanceName, newBestEffort.TimeS, workoutWithRoute.Id);
            }
            else if (newBestEffort.TimeS < existingBestEffort.TimeS)
            {
                // New workout has faster time, update
                // Ensure StartedAt is UTC (defensive check)
                var workoutDate = workoutWithRoute.StartedAt.Kind == DateTimeKind.Utc
                    ? workoutWithRoute.StartedAt
                    : DateTime.SpecifyKind(workoutWithRoute.StartedAt, DateTimeKind.Utc);
                
                _logger.LogDebug("Updating best effort for {Distance}: {OldTimeS}s -> {NewTimeS}s (workout {WorkoutId})", 
                    distanceName, existingBestEffort.TimeS, newBestEffort.TimeS, workoutWithRoute.Id);
                
                existingBestEffort.TimeS = newBestEffort.TimeS;
                existingBestEffort.WorkoutId = workoutWithRoute.Id;
                existingBestEffort.WorkoutDate = workoutDate;
                existingBestEffort.CalculatedAt = DateTime.UtcNow;
                updatedCount++;
            }
            else
            {
                // Existing best effort is faster, preserve it
                preservedCount++;
                _logger.LogDebug("Preserving existing best effort for {Distance}: {TimeS}s (new workout: {NewTimeS}s)", 
                    distanceName, existingBestEffort.TimeS, newBestEffort.TimeS);
            }
        }

        if (createdCount > 0 || updatedCount > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("Updated best efforts for workout {WorkoutId}: {Created} created, {Updated} updated, {Preserved} preserved", 
                workout.Id, createdCount, updatedCount, preservedCount);
        }
        else
        {
            _logger.LogDebug("No best efforts updated for workout {WorkoutId} (all preserved or none qualified)", workout.Id);
        }

        // Verification: Ensure all existing best efforts for unprocessed distances are still present
        // Only verify distances that actually existed before the update (intersect with allExistingBestEfforts)
        var unprocessedDistances = StandardDistances.Keys.Except(processedDistances).ToList();
        var unprocessedDistancesThatExisted = unprocessedDistances.Intersect(allExistingBestEfforts).ToList();
        
        if (unprocessedDistancesThatExisted.Any())
        {
            var verificationBestEfforts = await db.BestEfforts
                .AsNoTracking()
                .Where(be => unprocessedDistancesThatExisted.Contains(be.Distance))
                .Select(be => be.Distance)
                .ToListAsync();

            var missingDistances = unprocessedDistancesThatExisted.Except(verificationBestEfforts).ToList();
            if (missingDistances.Any())
            {
                _logger.LogWarning("Best efforts missing for unprocessed distances after update: {Distances}", 
                    string.Join(", ", missingDistances));
            }
            else
            {
                _logger.LogDebug("Verification passed: All {Count} unprocessed best efforts are preserved", 
                    verificationBestEfforts.Count);
            }
        }
    }

    /// <summary>
    /// Clear all stored best efforts from the database.
    /// </summary>
    public async Task ClearBestEffortsAsync(TempoDbContext db)
    {
        var allBestEfforts = await db.BestEfforts.ToListAsync();
        db.BestEfforts.RemoveRange(allBestEfforts);
        await db.SaveChangesAsync();
        _logger.LogInformation("Cleared {Count} best efforts from database", allBestEfforts.Count);
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

