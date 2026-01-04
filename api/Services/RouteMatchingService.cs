using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Service for finding similar routes and calculating route similarity scores.
/// </summary>
public class RouteMatchingService
{
    private readonly TempoDbContext _db;
    private readonly ILogger<RouteMatchingService> _logger;

    // Configuration constants
    private const double StartEndProximityThresholdM = 100.0; // Start/end points must be within 100m
    private const double DistanceSimilarityThresholdPercent = 0.10; // 10% difference allowed
    private const double RouteSimilarityThresholdM = 50.0; // Average distance < 50m = match
    private const double SamplingIntervalM = 100.0; // Sample every 100m along route
    private const int DefaultMaxYears = 2; // Limit to last 2 years

    public RouteMatchingService(TempoDbContext db, ILogger<RouteMatchingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Finds similar routes for a given workout.
    /// </summary>
    /// <param name="workoutId">The workout ID to find similar routes for</param>
    /// <param name="maxResults">Maximum number of results to return (default: 10)</param>
    /// <param name="maxYears">Maximum number of years to look back (default: 2)</param>
    /// <returns>List of similar route matches, sorted by similarity score</returns>
    public async Task<List<SimilarRouteMatch>> FindSimilarRoutesAsync(
        Guid workoutId,
        int maxResults = 10,
        int maxYears = DefaultMaxYears)
    {
        // Get the current workout without route first (to avoid JSON parsing errors)
        var currentWorkout = await _db.Workouts
            .FirstOrDefaultAsync(w => w.Id == workoutId);

        if (currentWorkout == null)
        {
            _logger.LogWarning("Workout {WorkoutId} not found", workoutId);
            return new List<SimilarRouteMatch>();
        }

        // Load route using raw SQL to get RouteGeoJson as text, avoiding JSONB validation errors
        RouteData? currentRouteData = null;
        try
        {
            currentRouteData = await _db.Database
                .SqlQueryRaw<RouteData>(
                    @"SELECT ""Id"", ""WorkoutId"", ""RouteGeoJson""::text as ""RouteGeoJson""
                      FROM ""WorkoutRoutes"" 
                      WHERE ""WorkoutId"" = {0}",
                    workoutId)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load route for current workout {WorkoutId} due to database error", workoutId);
            return new List<SimilarRouteMatch>();
        }

        if (currentRouteData == null || string.IsNullOrEmpty(currentRouteData.RouteGeoJson))
        {
            _logger.LogDebug("Workout {WorkoutId} has no route data", workoutId);
            return new List<SimilarRouteMatch>();
        }

        // Create WorkoutRoute object for consistency
        currentWorkout.Route = new WorkoutRoute
        {
            Id = currentRouteData.Id,
            WorkoutId = currentRouteData.WorkoutId,
            RouteGeoJson = currentRouteData.RouteGeoJson
        };

        // Extract coordinates from current route
        var currentRouteCoords = ExtractCoordinatesFromGeoJson(currentWorkout.Route.RouteGeoJson);
        if (currentRouteCoords.Count < 2)
        {
            _logger.LogWarning("Workout {WorkoutId} route has insufficient points ({Count})", workoutId, currentRouteCoords.Count);
            return new List<SimilarRouteMatch>();
        }

        // Calculate time range for filtering
        var minDate = currentWorkout.StartedAt.AddYears(-maxYears);
        var maxDate = currentWorkout.StartedAt; // Only include workouts before current workout

        // Get candidate workouts without routes first (to avoid JSON parsing errors during Include)
        var candidateWorkouts = await _db.Workouts
            .Where(w => w.Id != workoutId &&
                       w.StartedAt >= minDate &&
                       w.StartedAt < maxDate &&
                       _db.WorkoutRoutes.Any(r => r.WorkoutId == w.Id))
            .ToListAsync();

        // Load all route data in a single query to avoid N+1 query problem
        var candidateWorkoutIds = candidateWorkouts.Select(w => w.Id).ToList();
        Dictionary<Guid, RouteData> routeDataMap = new Dictionary<Guid, RouteData>();
        
        if (candidateWorkoutIds.Count > 0)
        {
            try
            {
                // Build IN clause with GUIDs - safe because IDs come from database, not user input
                var guidStrings = candidateWorkoutIds.Select(id => $"'{id}'").ToList();
                var inClause = string.Join(", ", guidStrings);
                
                var allRouteData = await _db.Database
                    .SqlQueryRaw<RouteData>(
                        $@"SELECT ""Id"", ""WorkoutId"", ""RouteGeoJson""::text as ""RouteGeoJson""
                          FROM ""WorkoutRoutes"" 
                          WHERE ""WorkoutId"" IN ({inClause})")
                    .ToListAsync();
                
                // Create dictionary for O(1) lookup
                foreach (var routeData in allRouteData)
                {
                    routeDataMap[routeData.WorkoutId] = routeData;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load routes for candidate workouts due to database error");
                // Continue with empty map - individual workouts will be skipped
            }
        }

        var matches = new List<SimilarRouteMatch>();

        foreach (var candidate in candidateWorkouts)
        {
            // Get route data from pre-loaded dictionary instead of querying database
            if (!routeDataMap.TryGetValue(candidate.Id, out var routeData) || 
                routeData == null || 
                string.IsNullOrEmpty(routeData.RouteGeoJson))
            {
                continue;
            }

            // Create a WorkoutRoute object for consistency with existing code
            var route = new WorkoutRoute
            {
                Id = routeData.Id,
                WorkoutId = routeData.WorkoutId,
                RouteGeoJson = routeData.RouteGeoJson
            };
            candidate.Route = route;

            // Quick filters: start/end proximity and distance similarity
            // PassQuickFilters extracts coordinates once and returns them if filters pass
            var (passed, candidateCoords) = PassQuickFilters(currentWorkout, candidate, currentRouteCoords);
            if (!passed || candidateCoords == null || candidateCoords.Count < 2)
            {
                continue;
            }

            // Calculate detailed similarity (candidateCoords already extracted in PassQuickFilters)

            var averageDistance = CalculateAveragePointDistance(currentRouteCoords, candidateCoords);
            var similarityScore = CalculateSimilarityScore(averageDistance);

            // Only include if similarity is above threshold
            if (averageDistance <= RouteSimilarityThresholdM)
            {
                matches.Add(new SimilarRouteMatch
                {
                    WorkoutId = candidate.Id,
                    StartedAt = candidate.StartedAt,
                    DurationS = candidate.DurationS,
                    DistanceM = candidate.DistanceM,
                    AvgPaceS = candidate.AvgPaceS,
                    SimilarityScore = similarityScore,
                    AverageDistanceM = averageDistance
                });
            }
        }

        // Sort by similarity score (highest first), then by date (most recent first), then by distance similarity
        var sortedMatches = matches
            .OrderByDescending(m => m.SimilarityScore)
            .ThenByDescending(m => m.StartedAt)
            .ThenBy(m => Math.Abs(m.DistanceM - currentWorkout.DistanceM))
            .Take(maxResults)
            .ToList();

        return sortedMatches;
    }

    /// <summary>
    /// Calculates route similarity score between two routes.
    /// </summary>
    /// <param name="route1">First route</param>
    /// <param name="route2">Second route</param>
    /// <returns>Similarity score (0-100, higher = more similar)</returns>
    public double CalculateRouteSimilarity(WorkoutRoute route1, WorkoutRoute route2)
    {
        var coords1 = ExtractCoordinatesFromGeoJson(route1.RouteGeoJson);
        var coords2 = ExtractCoordinatesFromGeoJson(route2.RouteGeoJson);

        if (coords1.Count < 2 || coords2.Count < 2)
        {
            return 0.0;
        }

        var averageDistance = CalculateAveragePointDistance(coords1, coords2);
        return CalculateSimilarityScore(averageDistance);
    }

    /// <summary>
    /// Checks if two routes are similar based on threshold.
    /// </summary>
    /// <param name="route1">First route</param>
    /// <param name="route2">Second route</param>
    /// <param name="threshold">Distance threshold in meters (default: 50.0)</param>
    /// <returns>True if routes are similar, false otherwise</returns>
    public bool AreRoutesSimilar(WorkoutRoute route1, WorkoutRoute route2, double threshold = RouteSimilarityThresholdM)
    {
        var coords1 = ExtractCoordinatesFromGeoJson(route1.RouteGeoJson);
        var coords2 = ExtractCoordinatesFromGeoJson(route2.RouteGeoJson);

        if (coords1.Count < 2 || coords2.Count < 2)
        {
            return false;
        }

        var averageDistance = CalculateAveragePointDistance(coords1, coords2);
        return averageDistance <= threshold;
    }

    /// <summary>
    /// Extracts coordinates from GeoJSON LineString format.
    /// </summary>
    /// <param name="routeGeoJson">GeoJSON string</param>
    /// <returns>List of (latitude, longitude) tuples</returns>
    public List<(double lat, double lon)> ExtractCoordinatesFromGeoJson(string routeGeoJson)
    {
        var coordinates = new List<(double lat, double lon)>();

        try
        {
            var geoJson = JsonSerializer.Deserialize<JsonElement>(routeGeoJson);

            // Handle null or invalid JSON
            if (geoJson.ValueKind == JsonValueKind.Null)
            {
                return coordinates;
            }

            if (!geoJson.TryGetProperty("coordinates", out var coordinatesElement))
            {
                return coordinates;
            }

            foreach (var coordElement in coordinatesElement.EnumerateArray())
            {
                if (coordElement.ValueKind != JsonValueKind.Array || coordElement.GetArrayLength() < 2)
                {
                    continue;
                }

                var coords = coordElement.EnumerateArray().ToArray();
                if (coords.Length < 2)
                {
                    continue;
                }

                // GeoJSON format: [longitude, latitude]
                var longitude = coords[0].GetDouble();
                var latitude = coords[1].GetDouble();

                coordinates.Add((latitude, longitude));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract coordinates from GeoJSON");
        }

        return coordinates;
    }

    /// <summary>
    /// Calculates average point-to-point distance between two routes.
    /// Routes are sampled at regular intervals and corresponding points are compared.
    /// </summary>
    /// <param name="route1">First route coordinates</param>
    /// <param name="route2">Second route coordinates</param>
    /// <returns>Average distance in meters</returns>
    public double CalculateAveragePointDistance(
        List<(double lat, double lon)> route1,
        List<(double lat, double lon)> route2)
    {
        if (route1.Count < 2 || route2.Count < 2)
        {
            return double.MaxValue;
        }

        // Calculate total distance for each route to determine sampling points
        var route1Distance = CalculateRouteDistance(route1);
        var route2Distance = CalculateRouteDistance(route2);

        // Sample points along both routes at regular intervals
        var sampledRoute1 = SampleRouteAtIntervals(route1, route1Distance, SamplingIntervalM);
        var sampledRoute2 = SampleRouteAtIntervals(route2, route2Distance, SamplingIntervalM);

        // Use the route with fewer sampled points as the reference
        var referenceRoute = sampledRoute1.Count <= sampledRoute2.Count ? sampledRoute1 : sampledRoute2;
        var comparisonRoute = sampledRoute1.Count <= sampledRoute2.Count ? sampledRoute2 : sampledRoute1;

        if (referenceRoute.Count == 0)
        {
            return double.MaxValue;
        }

        // For each point in reference route, find nearest point in comparison route
        var distances = new List<double>();
        foreach (var refPoint in referenceRoute)
        {
            var minDistance = double.MaxValue;
            foreach (var compPoint in comparisonRoute)
            {
                var distance = GeoUtils.HaversineDistance(
                    refPoint.lat, refPoint.lon,
                    compPoint.lat, compPoint.lon);
                minDistance = Math.Min(minDistance, distance);
            }
            distances.Add(minDistance);
        }

        return distances.Count > 0 ? distances.Average() : double.MaxValue;
    }

    /// <summary>
    /// Checks if candidate workout passes quick filters (start/end proximity, distance similarity).
    /// Returns both the filter result and the extracted coordinates to avoid re-parsing GeoJSON.
    /// </summary>
    private (bool passed, List<(double lat, double lon)>? coords) PassQuickFilters(
        Workout currentWorkout,
        Workout candidate,
        List<(double lat, double lon)> currentRouteCoords)
    {
        // Filter by distance similarity (within 10%)
        var distanceDiff = Math.Abs(candidate.DistanceM - currentWorkout.DistanceM);
        var distanceThreshold = currentWorkout.DistanceM * DistanceSimilarityThresholdPercent;
        if (distanceDiff > distanceThreshold)
        {
            return (false, null);
        }

        // Extract candidate route coordinates
        if (candidate.Route == null || string.IsNullOrEmpty(candidate.Route.RouteGeoJson))
        {
            return (false, null);
        }

        var candidateCoords = ExtractCoordinatesFromGeoJson(candidate.Route.RouteGeoJson);
        if (candidateCoords.Count < 2)
        {
            return (false, null);
        }

        // Check start point proximity
        var startDistance = GeoUtils.HaversineDistance(
            currentRouteCoords[0].lat, currentRouteCoords[0].lon,
            candidateCoords[0].lat, candidateCoords[0].lon);

        if (startDistance > StartEndProximityThresholdM)
        {
            return (false, null);
        }

        // Check end point proximity
        var endDistance = GeoUtils.HaversineDistance(
            currentRouteCoords[^1].lat, currentRouteCoords[^1].lon,
            candidateCoords[^1].lat, candidateCoords[^1].lon);

        if (endDistance > StartEndProximityThresholdM)
        {
            return (false, null);
        }

        return (true, candidateCoords);
    }

    /// <summary>
    /// Calculates total distance of a route by summing segment distances.
    /// </summary>
    private double CalculateRouteDistance(List<(double lat, double lon)> route)
    {
        if (route.Count < 2)
        {
            return 0.0;
        }

        double totalDistance = 0.0;
        for (int i = 1; i < route.Count; i++)
        {
            totalDistance += GeoUtils.HaversineDistance(
                route[i - 1].lat, route[i - 1].lon,
                route[i].lat, route[i].lon);
        }

        return totalDistance;
    }

    /// <summary>
    /// Samples route at regular distance intervals.
    /// </summary>
    private List<(double lat, double lon)> SampleRouteAtIntervals(
        List<(double lat, double lon)> route,
        double totalDistance,
        double intervalM)
    {
        if (route.Count < 2 || totalDistance <= 0)
        {
            return new List<(double lat, double lon)> { route[0] };
        }

        var sampled = new List<(double lat, double lon)> { route[0] }; // Always include start

        // Calculate cumulative distances
        var cumulativeDistances = new List<double> { 0.0 };
        double cumulative = 0.0;
        for (int i = 1; i < route.Count; i++)
        {
            cumulative += GeoUtils.HaversineDistance(
                route[i - 1].lat, route[i - 1].lon,
                route[i].lat, route[i].lon);
            cumulativeDistances.Add(cumulative);
        }

        // Sample at intervals
        double targetDistance = intervalM;
        while (targetDistance < totalDistance)
        {
            // Find the point closest to target distance
            var closestIndex = 0;
            var minDiff = double.MaxValue;
            for (int i = 0; i < cumulativeDistances.Count; i++)
            {
                var diff = Math.Abs(cumulativeDistances[i] - targetDistance);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestIndex = i;
                }
            }

            sampled.Add(route[closestIndex]);
            targetDistance += intervalM;
        }

        // Always include end point
        if (route.Count > 1 && !sampled.Contains(route[^1]))
        {
            sampled.Add(route[^1]);
        }

        return sampled;
    }

    /// <summary>
    /// Converts average distance to similarity score (0-100).
    /// </summary>
    private double CalculateSimilarityScore(double averageDistanceM)
    {
        // Similarity score = 100 - (average distance in meters), clamped to 0-100
        var score = 100.0 - averageDistanceM;
        return Math.Max(0.0, Math.Min(100.0, score));
    }

    /// <summary>
    /// Helper class for raw SQL query results when loading routes with potentially invalid JSON.
    /// </summary>
    private class RouteData
    {
        public Guid Id { get; set; }
        public Guid WorkoutId { get; set; }
        public string RouteGeoJson { get; set; } = string.Empty;
    }
}

/// <summary>
/// Represents a similar route match result.
/// </summary>
public class SimilarRouteMatch
{
    public Guid WorkoutId { get; set; }
    public DateTime StartedAt { get; set; }
    public int DurationS { get; set; }
    public double DistanceM { get; set; }
    public int AvgPaceS { get; set; }
    public double SimilarityScore { get; set; }
    public double AverageDistanceM { get; set; }
}

