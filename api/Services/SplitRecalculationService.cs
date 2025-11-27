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

        // First, try to extract from RawGpxData
        if (!string.IsNullOrEmpty(workout.RawGpxData))
        {
            trackPoints = ExtractTrackPointsFromRawGpxData(workout.RawGpxData);
        }

        // If not found, try to re-parse RawFileData
        if (trackPoints == null && workout.RawFileData != null && workout.RawFileData.Length > 0)
        {
            trackPoints = await ReparseTrackPointsFromRawFileAsync(workout);
        }

        // If still not found, extract from RouteGeoJson (coordinates only, no timestamps)
        if (trackPoints == null)
        {
            trackPoints = ExtractTrackPointsFromRouteGeoJson(workout.Route.RouteGeoJson);
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
        var workouts = await _db.Workouts
            .Include(w => w.Route)
            .Where(w => w.Route != null)
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
        try
        {
            var rawGpx = JsonSerializer.Deserialize<JsonElement>(rawGpxDataJson);
            if (!rawGpx.TryGetProperty("trackPoints", out var trackPointsElement))
            {
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

                trackPoints.Add(point);
            }

            return trackPoints.Count > 0 ? trackPoints : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract track points from RawGpxData");
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

