using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Service for recalculating workout splits based on unit preference.
/// </summary>
public class SplitRecalculationService
{
    private readonly TempoDbContext _db;
    private readonly GpxParserService _gpxParser;
    private readonly FitParserService _fitParser;
    private readonly ILogger<SplitRecalculationService> _logger;

    public SplitRecalculationService(
        TempoDbContext db,
        GpxParserService gpxParser,
        FitParserService fitParser,
        ILogger<SplitRecalculationService> logger)
    {
        _db = db;
        _gpxParser = gpxParser;
        _fitParser = fitParser;
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

        // Calculate split distance based on unit preference
        var splitDistanceMeters = unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase)
            ? 1609.344
            : 1000.0;

        // Try to extract track points from raw data
        List<GpxParserService.GpxPoint>? trackPoints = null;

        // First, try to extract from RawGpxData (GPX files)
        if (!string.IsNullOrEmpty(workout.RawGpxData))
        {
            trackPoints = ExtractTrackPointsFromRawGpxData(workout.RawGpxData);
        }

        // If not found, try to extract from RawFitData (FIT files)
        if (trackPoints == null && !string.IsNullOrEmpty(workout.RawFitData))
        {
            trackPoints = ExtractTrackPointsFromRawFitData(workout.RawFitData);
        }

        // If not found, try to re-parse RawFileData
        if (trackPoints == null && workout.RawFileData != null && workout.RawFileData.Length > 0)
        {
            trackPoints = await ReparseTrackPointsFromRawFileAsync(workout);
        }

        // If still not found, extract from RouteGeoJson and reconstruct timestamps
        if (trackPoints == null)
        {
            _logger.LogInformation("Workout {WorkoutId} using RouteGeoJson, reconstructing timestamps from distance", workout.Id);
            trackPoints = ExtractTrackPointsFromRouteGeoJson(workout.Route.RouteGeoJson);
            
            // Reconstruct timestamps based on cumulative distance and workout duration
            if (trackPoints != null && trackPoints.Count > 1)
            {
                trackPoints = ReconstructTimestampsFromDistance(trackPoints, workout.StartedAt, workout.DurationS, workout.DistanceM);
            }
        }

        if (trackPoints == null || trackPoints.Count < 2)
        {
            _logger.LogWarning("Workout {WorkoutId} has insufficient track point data, skipping split recalculation", workout.Id);
            return false;
        }

        // Delete existing splits
        var existingSplits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workout.Id)
            .ToListAsync();
        
        if (existingSplits.Count > 0)
        {
            _db.WorkoutSplits.RemoveRange(existingSplits);
        }

        // Calculate new splits
        var newSplits = _gpxParser.CalculateSplits(
            trackPoints,
            workout.DistanceM,
            workout.DurationS,
            splitDistanceMeters
        );

        // Set workout ID for each split
        foreach (var split in newSplits)
        {
            split.WorkoutId = workout.Id;
        }

        // Add new splits
        _db.WorkoutSplits.AddRange(newSplits);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Recalculated splits for workout {WorkoutId}: {OldCount} -> {NewCount} splits", 
            workout.Id, existingSplits.Count, newSplits.Count);

        return true;
    }

    /// <summary>
    /// Recalculates splits for all workouts that have route data.
    /// </summary>
    public async Task<SplitRecalculationResult> RecalculateSplitsForAllWorkoutsAsync(string unitPreference)
    {
        // Explicitly select all fields needed for recalculation, including large fields that EF Core might not load by default
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
    /// Extracts track points from RawGpxData JSON.
    /// </summary>
    private List<GpxParserService.GpxPoint>? ExtractTrackPointsFromRawGpxData(string rawGpxDataJson)
    {
        return ExtractTrackPointsFromJsonData(rawGpxDataJson, "RawGpxData");
    }

    /// <summary>
    /// Extracts track points from RawFitData JSON.
    /// </summary>
    private List<GpxParserService.GpxPoint>? ExtractTrackPointsFromRawFitData(string rawFitDataJson)
    {
        return ExtractTrackPointsFromJsonData(rawFitDataJson, "RawFitData");
    }

    /// <summary>
    /// Extracts track points from JSON data (shared implementation for both GPX and FIT).
    /// </summary>
    private List<GpxParserService.GpxPoint>? ExtractTrackPointsFromJsonData(string jsonData, string dataType)
    {
        try
        {
            var rawData = JsonSerializer.Deserialize<JsonElement>(jsonData);
            if (!rawData.TryGetProperty("trackPoints", out var trackPointsElement))
            {
                _logger.LogWarning("{DataType} is missing 'trackPoints' property", dataType);
                return null;
            }

            var trackPoints = new List<GpxParserService.GpxPoint>();
            foreach (var pointElement in trackPointsElement.EnumerateArray())
            {
                if (!pointElement.TryGetProperty("lat", out var latElement) ||
                    !pointElement.TryGetProperty("lon", out var lonElement))
                {
                    continue;
                }

                var point = new GpxParserService.GpxPoint
                {
                    Latitude = latElement.GetDouble(),
                    Longitude = lonElement.GetDouble()
                };

                if (pointElement.TryGetProperty("ele", out var eleElement) && eleElement.ValueKind == JsonValueKind.Number)
                {
                    point.Elevation = eleElement.GetDouble();
                }

                if (pointElement.TryGetProperty("time", out var timeElement) && timeElement.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(timeElement.GetString(), out var time))
                    {
                        point.Time = DateTime.SpecifyKind(time, DateTimeKind.Utc);
                    }
                }

                // Extract heart rate
                if (pointElement.TryGetProperty("hr", out var hrElement) && hrElement.ValueKind == JsonValueKind.Number)
                {
                    point.HeartRateBpm = (byte)hrElement.GetInt32();
                }

                // Extract cadence
                if (pointElement.TryGetProperty("cad", out var cadElement) && cadElement.ValueKind == JsonValueKind.Number)
                {
                    point.CadenceRpm = (byte)cadElement.GetInt32();
                }

                // Extract power
                if (pointElement.TryGetProperty("power", out var powerElement) && powerElement.ValueKind == JsonValueKind.Number)
                {
                    point.PowerWatts = (ushort)powerElement.GetInt32();
                }

                // Extract temperature
                if (pointElement.TryGetProperty("temp", out var tempElement) && tempElement.ValueKind == JsonValueKind.Number)
                {
                    point.TemperatureC = (sbyte)tempElement.GetInt32();
                }

                trackPoints.Add(point);
            }

            _logger.LogInformation("Extracted {Count} track points from {DataType}", trackPoints.Count, dataType);
            return trackPoints.Count > 0 ? trackPoints : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract track points from {DataType}", dataType);
            return null;
        }
    }

    /// <summary>
    /// Re-parses track points from RawFileData if it's a GPX or FIT file.
    /// </summary>
    private async Task<List<GpxParserService.GpxPoint>?> ReparseTrackPointsFromRawFileAsync(Workout workout)
    {
        if (workout.RawFileData == null || workout.RawFileData.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(workout.RawFileData);

            if (workout.RawFileType == "gpx")
            {
                var parseResult = _gpxParser.ParseGpx(stream);
                return parseResult.TrackPoints;
            }
            else if (workout.RawFileType == "fit")
            {
                // Check if it's gzipped by looking at the first bytes
                var isGzipped = workout.RawFileName?.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase) == true;
                
                FitParserService.FitParseResult parseResult;
                if (isGzipped)
                {
                    parseResult = _fitParser.ParseGzippedFit(stream);
                }
                else
                {
                    parseResult = _fitParser.ParseFit(stream);
                }
                return parseResult.TrackPoints;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-parse track points from RawFileData for workout {WorkoutId}", workout.Id);
        }

        return null;
    }

    /// <summary>
    /// Extracts track points from RouteGeoJson (coordinates only, no timestamps or elevation).
    /// </summary>
    private List<GpxParserService.GpxPoint>? ExtractTrackPointsFromRouteGeoJson(string routeGeoJson)
    {
        try
        {
            var geoJson = JsonSerializer.Deserialize<JsonElement>(routeGeoJson);
            if (!geoJson.TryGetProperty("type", out var typeElement) ||
                typeElement.GetString() != "LineString")
            {
                return null;
            }

            if (!geoJson.TryGetProperty("coordinates", out var coordinatesElement))
            {
                return null;
            }

            var trackPoints = new List<GpxParserService.GpxPoint>();
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

                var point = new GpxParserService.GpxPoint
                {
                    Longitude = coords[0].GetDouble(),
                    Latitude = coords[1].GetDouble()
                };

                // GeoJSON coordinates may have elevation as third element
                if (coords.Length >= 3 && coords[2].ValueKind == JsonValueKind.Number)
                {
                    point.Elevation = coords[2].GetDouble();
                }

                trackPoints.Add(point);
            }

            return trackPoints.Count > 0 ? trackPoints : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract track points from RouteGeoJson");
            return null;
        }
    }

    /// <summary>
    /// Reconstructs timestamps for track points based on cumulative distance along the route.
    /// This allows accurate split calculation for workouts without stored timestamps.
    /// </summary>
    private List<GpxParserService.GpxPoint> ReconstructTimestampsFromDistance(
        List<GpxParserService.GpxPoint> trackPoints,
        DateTime workoutStartTime,
        int workoutDurationS,
        double workoutDistanceM)
    {
        if (trackPoints == null || trackPoints.Count < 2)
        {
            return trackPoints;
        }

        // Calculate cumulative distances between consecutive points
        var cumulativeDistances = new List<double> { 0.0 };
        double totalDistance = 0.0;

        for (int i = 1; i < trackPoints.Count; i++)
        {
            var prevPoint = trackPoints[i - 1];
            var currPoint = trackPoints[i];
            
            // Calculate distance using Haversine formula
            var distance = CalculateHaversineDistance(
                prevPoint.Latitude, prevPoint.Longitude,
                currPoint.Latitude, currPoint.Longitude);
            
            totalDistance += distance;
            cumulativeDistances.Add(totalDistance);
        }

        // Assign timestamps proportionally based on distance
        var reconstructed = new List<GpxParserService.GpxPoint>();
        for (int i = 0; i < trackPoints.Count; i++)
        {
            var point = trackPoints[i];
            var distanceRatio = totalDistance > 0 ? cumulativeDistances[i] / totalDistance : 0.0;
            var elapsedSeconds = distanceRatio * workoutDurationS;
            
            point.Time = workoutStartTime.AddSeconds(elapsedSeconds);
            reconstructed.Add(point);
        }

        return reconstructed;
    }

    /// <summary>
    /// Calculates distance between two GPS coordinates using the Haversine formula.
    /// </summary>
    private double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth's radius in meters
        
        var lat1Rad = lat1 * Math.PI / 180.0;
        var lat2Rad = lat2 * Math.PI / 180.0;
        var deltaLat = (lat2 - lat1) * Math.PI / 180.0;
        var deltaLon = (lon2 - lon1) * Math.PI / 180.0;
        
        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        
        return R * c;
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

