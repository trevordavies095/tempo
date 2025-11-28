using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Service for cropping/trimming workouts by removing time from the beginning or end.
/// </summary>
public class WorkoutCropService
{
    private readonly TempoDbContext _db;
    private readonly ILogger<WorkoutCropService> _logger;
    private const int MinimumRemainingDurationSeconds = 10;

    public WorkoutCropService(
        TempoDbContext db,
        ILogger<WorkoutCropService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Crops a workout by removing time from the beginning and/or end.
    /// </summary>
    public async Task<Workout> CropWorkoutAsync(
        Workout workout,
        int startTrimSeconds,
        int endTrimSeconds)
    {
        if (workout.Route == null)
        {
            throw new InvalidOperationException("Workout has no route data. Cannot crop workout without route.");
        }

        var originalDurationS = workout.DurationS;
        var newDurationS = originalDurationS - startTrimSeconds - endTrimSeconds;

        if (newDurationS < MinimumRemainingDurationSeconds)
        {
            throw new InvalidOperationException(
                $"Cropping would result in a workout shorter than {MinimumRemainingDurationSeconds} seconds. " +
                $"Original duration: {originalDurationS}s, Trim: {startTrimSeconds}s start + {endTrimSeconds}s end = {startTrimSeconds + endTrimSeconds}s");
        }

        _logger.LogInformation(
            "Cropping workout {WorkoutId}: Original duration {OriginalDuration}s, " +
            "Trimming {StartTrim}s from start and {EndTrim}s from end, " +
            "New duration: {NewDuration}s",
            workout.Id, originalDurationS, startTrimSeconds, endTrimSeconds, newDurationS);

        // Load time series data
        var timeSeries = await _db.WorkoutTimeSeries
            .Where(ts => ts.WorkoutId == workout.Id)
            .OrderBy(ts => ts.ElapsedSeconds)
            .ToListAsync();

        // Filter and reindex time series
        var croppedTimeSeries = await CropTimeSeriesAsync(
            workout.Id,
            timeSeries,
            startTrimSeconds,
            endTrimSeconds,
            originalDurationS);

        // Trim route coordinates
        var croppedRoute = CropRoute(workout.Route, timeSeries, startTrimSeconds, endTrimSeconds, originalDurationS);

        // Recalculate aggregates from cropped data
        RecalculateAggregates(workout, croppedTimeSeries, croppedRoute, startTrimSeconds, newDurationS);

        // Update workout fields
        workout.DurationS = newDurationS;
        workout.StartedAt = workout.StartedAt.AddSeconds(startTrimSeconds);
        workout.AvgPaceS = newDurationS > 0 && workout.DistanceM > 0
            ? (int)(newDurationS / (workout.DistanceM / 1000.0))
            : 0;

        // Update route
        workout.Route.RouteGeoJson = croppedRoute;

        // Delete old time series and add new ones
        if (timeSeries.Count > 0)
        {
            _db.WorkoutTimeSeries.RemoveRange(timeSeries);
        }
        if (croppedTimeSeries.Count > 0)
        {
            _db.WorkoutTimeSeries.AddRange(croppedTimeSeries);
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Successfully cropped workout {WorkoutId}: Duration {NewDuration}s, Distance {Distance}m",
            workout.Id, newDurationS, workout.DistanceM);

        return workout;
    }

    /// <summary>
    /// Filters and reindexes time series data based on crop parameters.
    /// </summary>
    private async Task<List<WorkoutTimeSeries>> CropTimeSeriesAsync(
        Guid workoutId,
        List<WorkoutTimeSeries> timeSeries,
        int startTrimSeconds,
        int endTrimSeconds,
        int originalDurationS)
    {
        var endTimeThreshold = originalDurationS - endTrimSeconds;

        var cropped = timeSeries
            .Where(ts => ts.ElapsedSeconds >= startTrimSeconds && ts.ElapsedSeconds <= endTimeThreshold)
            .Select(ts => new WorkoutTimeSeries
            {
                Id = Guid.NewGuid(),
                WorkoutId = workoutId,
                ElapsedSeconds = ts.ElapsedSeconds - startTrimSeconds,
                DistanceM = ts.DistanceM.HasValue && timeSeries.Count > 0
                    ? CalculateCroppedDistance(ts.DistanceM.Value, timeSeries, startTrimSeconds)
                    : ts.DistanceM,
                HeartRateBpm = ts.HeartRateBpm,
                CadenceRpm = ts.CadenceRpm,
                PowerWatts = ts.PowerWatts,
                SpeedMps = ts.SpeedMps,
                GradePercent = ts.GradePercent,
                ElevationM = ts.ElevationM,
                TemperatureC = ts.TemperatureC,
                VerticalSpeedMps = ts.VerticalSpeedMps
            })
            .ToList();

        return cropped;
    }

    /// <summary>
    /// Calculates the cropped distance by subtracting the distance at the start trim point.
    /// </summary>
    private double? CalculateCroppedDistance(
        double distanceAtPoint,
        List<WorkoutTimeSeries> timeSeries,
        int startTrimSeconds)
    {
        // Find the distance at the start trim point (or closest point before it)
        var startPoint = timeSeries
            .Where(ts => ts.ElapsedSeconds <= startTrimSeconds && ts.DistanceM.HasValue)
            .OrderByDescending(ts => ts.ElapsedSeconds)
            .FirstOrDefault();

        if (startPoint?.DistanceM.HasValue == true)
        {
            var startDistance = startPoint.DistanceM.Value;
            return Math.Max(0, distanceAtPoint - startDistance);
        }

        // If we can't find a start point, return null (will be recalculated from route)
        return null;
    }

    /// <summary>
    /// Trims route coordinates based on crop parameters.
    /// </summary>
    private string CropRoute(
        WorkoutRoute route,
        List<WorkoutTimeSeries> timeSeries,
        int startTrimSeconds,
        int endTrimSeconds,
        int originalDurationS)
    {
        try
        {
            var geoJson = JsonSerializer.Deserialize<JsonElement>(route.RouteGeoJson);
            if (!geoJson.TryGetProperty("coordinates", out var coordinatesElement))
            {
                throw new InvalidOperationException("Route GeoJSON does not contain coordinates");
            }

            var coordinates = coordinatesElement.EnumerateArray().ToList();
            if (coordinates.Count == 0)
            {
                return route.RouteGeoJson;
            }

            // Determine start and end indices for cropping
            int startIndex = 0;
            int endIndex = coordinates.Count - 1;

            if (timeSeries.Count > 0)
            {
                // Map time-based crop to coordinate indices using time series
                startIndex = FindCoordinateIndexFromTime(timeSeries, startTrimSeconds, coordinates.Count);
                endIndex = FindCoordinateIndexFromTime(timeSeries, originalDurationS - endTrimSeconds, coordinates.Count);
            }
            else
            {
                // No time series: estimate based on time ratio
                var startRatio = (double)startTrimSeconds / originalDurationS;
                var endRatio = (double)(originalDurationS - endTrimSeconds) / originalDurationS;
                startIndex = (int)Math.Floor(startRatio * coordinates.Count);
                endIndex = (int)Math.Ceiling(endRatio * coordinates.Count) - 1;
            }

            // Ensure valid indices
            startIndex = Math.Max(0, Math.Min(startIndex, coordinates.Count - 1));
            endIndex = Math.Max(startIndex, Math.Min(endIndex, coordinates.Count - 1));

            // Extract cropped coordinates
            var croppedCoordinates = new List<JsonElement>();
            for (int i = startIndex; i <= endIndex; i++)
            {
                croppedCoordinates.Add(coordinates[i]);
            }

            // Rebuild GeoJSON
            var croppedGeoJson = new
            {
                type = "LineString",
                coordinates = croppedCoordinates.Select(c => c.EnumerateArray().Select(e => e.GetDouble()).ToArray()).ToArray()
            };

            return JsonSerializer.Serialize(croppedGeoJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to crop route coordinates, returning original route");
            return route.RouteGeoJson;
        }
    }

    /// <summary>
    /// Finds the coordinate index corresponding to a specific elapsed time.
    /// </summary>
    private int FindCoordinateIndexFromTime(
        List<WorkoutTimeSeries> timeSeries,
        int targetElapsedSeconds,
        int totalCoordinates)
    {
        // Find the time series point closest to the target time
        var closestPoint = timeSeries
            .OrderBy(ts => Math.Abs(ts.ElapsedSeconds - targetElapsedSeconds))
            .FirstOrDefault();

        if (closestPoint == null)
        {
            // Fallback: estimate based on time ratio
            var timeRatio = (double)targetElapsedSeconds / (timeSeries.LastOrDefault()?.ElapsedSeconds ?? 1);
            return (int)Math.Floor(timeRatio * totalCoordinates);
        }

        // Map time series index to coordinate index
        var timeSeriesIndex = timeSeries.IndexOf(closestPoint);
        var indexRatio = (double)timeSeriesIndex / timeSeries.Count;
        return (int)Math.Floor(indexRatio * totalCoordinates);
    }

    /// <summary>
    /// Recalculates workout aggregates from cropped time series and route data.
    /// </summary>
    private void RecalculateAggregates(
        Workout workout,
        List<WorkoutTimeSeries> croppedTimeSeries,
        string croppedRouteJson,
        int startTrimSeconds,
        int newDurationS)
    {
        // Recalculate distance from route
        try
        {
            var geoJson = JsonSerializer.Deserialize<JsonElement>(croppedRouteJson);
            if (geoJson.TryGetProperty("coordinates", out var coordinatesElement))
            {
                var coordinates = coordinatesElement.EnumerateArray().ToList();
                double totalDistance = 0.0;

                for (int i = 1; i < coordinates.Count; i++)
                {
                    var prevCoord = coordinates[i - 1].EnumerateArray().ToArray();
                    var currCoord = coordinates[i].EnumerateArray().ToArray();

                    if (prevCoord.Length >= 2 && currCoord.Length >= 2)
                    {
                        var distance = GeoUtils.HaversineDistance(
                            prevCoord[1].GetDouble(), // latitude
                            prevCoord[0].GetDouble(), // longitude
                            currCoord[1].GetDouble(),
                            currCoord[0].GetDouble()
                        );
                        totalDistance += distance;
                    }
                }

                workout.DistanceM = totalDistance;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to recalculate distance from route, using time series data");
        }

        // If we have time series data, use it for distance and other metrics
        if (croppedTimeSeries.Count > 0)
        {
            var lastPoint = croppedTimeSeries.LastOrDefault(ts => ts.DistanceM.HasValue);
            if (lastPoint?.DistanceM.HasValue == true)
            {
                workout.DistanceM = lastPoint.DistanceM.Value;
            }

            // Recalculate heart rate stats
            var heartRates = croppedTimeSeries
                .Where(ts => ts.HeartRateBpm.HasValue)
                .Select(ts => (int)ts.HeartRateBpm!.Value)
                .ToList();

            if (heartRates.Count > 0)
            {
                workout.MaxHeartRateBpm = (byte)heartRates.Max();
                workout.MinHeartRateBpm = (byte)heartRates.Min();
                workout.AvgHeartRateBpm = (byte)Math.Round(heartRates.Average());
            }

            // Recalculate cadence stats
            var cadences = croppedTimeSeries
                .Where(ts => ts.CadenceRpm.HasValue)
                .Select(ts => (int)ts.CadenceRpm!.Value)
                .ToList();

            if (cadences.Count > 0)
            {
                workout.MaxCadenceRpm = (byte)cadences.Max();
                workout.AvgCadenceRpm = (byte)Math.Round(cadences.Average());
            }

            // Recalculate power stats
            var powers = croppedTimeSeries
                .Where(ts => ts.PowerWatts.HasValue)
                .Select(ts => (int)ts.PowerWatts!.Value)
                .ToList();

            if (powers.Count > 0)
            {
                workout.MaxPowerWatts = (ushort)powers.Max();
                workout.AvgPowerWatts = (ushort)Math.Round(powers.Average());
            }

            // Recalculate speed stats
            var speeds = croppedTimeSeries
                .Where(ts => ts.SpeedMps.HasValue)
                .Select(ts => ts.SpeedMps!.Value)
                .ToList();

            if (speeds.Count > 0)
            {
                workout.MaxSpeedMps = speeds.Max();
                workout.AvgSpeedMps = speeds.Average();
            }

            // Recalculate elevation stats
            var elevations = croppedTimeSeries
                .Where(ts => ts.ElevationM.HasValue)
                .Select(ts => ts.ElevationM!.Value)
                .ToList();

            if (elevations.Count > 0)
            {
                workout.MinElevM = elevations.Min();
                workout.MaxElevM = elevations.Max();

                // Calculate elevation gain/loss from cropped data
                double elevationGain = 0.0;
                double elevationLoss = 0.0;
                double? lastElevation = null;

                foreach (var elevation in elevations)
                {
                    if (lastElevation.HasValue)
                    {
                        var diff = elevation - lastElevation.Value;
                        if (diff > 0)
                        {
                            elevationGain += diff;
                        }
                        else if (diff < 0)
                        {
                            elevationLoss += Math.Abs(diff);
                        }
                    }
                    lastElevation = elevation;
                }

                workout.ElevGainM = elevationGain > 0 ? elevationGain : null;
                workout.ElevLossM = elevationLoss > 0 ? elevationLoss : null;
            }
        }
        else
        {
            // No time series: try to recalculate elevation from route
            try
            {
                var geoJson = JsonSerializer.Deserialize<JsonElement>(croppedRouteJson);
                if (geoJson.TryGetProperty("coordinates", out var coordinatesElement))
                {
                    var coordinates = coordinatesElement.EnumerateArray().ToList();
                    var elevations = new List<double>();

                    foreach (var coord in coordinates)
                    {
                        var coordArray = coord.EnumerateArray().ToArray();
                        if (coordArray.Length >= 3)
                        {
                            elevations.Add(coordArray[2].GetDouble());
                        }
                    }

                    if (elevations.Count > 0)
                    {
                        workout.MinElevM = elevations.Min();
                        workout.MaxElevM = elevations.Max();

                        // Simple elevation gain/loss calculation
                        double elevationGain = 0.0;
                        double elevationLoss = 0.0;
                        double? lastElevation = null;

                        foreach (var elevation in elevations)
                        {
                            if (lastElevation.HasValue)
                            {
                                var diff = elevation - lastElevation.Value;
                                if (diff > 0)
                                {
                                    elevationGain += diff;
                                }
                                else if (diff < 0)
                                {
                                    elevationLoss += Math.Abs(diff);
                                }
                            }
                            lastElevation = elevation;
                        }

                        workout.ElevGainM = elevationGain > 0 ? elevationGain : null;
                        workout.ElevLossM = elevationLoss > 0 ? elevationLoss : null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recalculate elevation from route");
            }
        }
    }
}

