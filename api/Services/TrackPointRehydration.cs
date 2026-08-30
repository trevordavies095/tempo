using System.Globalization;
using System.Text.Json;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Stored Workout fields → TrackPoints. No DbContext; geometry stays downstream.
/// </summary>
public class TrackPointRehydration
{
    private readonly GpxParserService _gpxParser;
    private readonly FitParserService _fitParser;
    private readonly ILogger<TrackPointRehydration> _logger;

    public TrackPointRehydration(
        GpxParserService gpxParser,
        FitParserService fitParser,
        ILogger<TrackPointRehydration> logger)
    {
        _gpxParser = gpxParser;
        _fitParser = fitParser;
        _logger = logger;
    }

    public List<TrackPoint>? Rehydrate(Workout workout)
    {
        List<TrackPoint>? trackPoints = null;

        if (!string.IsNullOrEmpty(workout.RawGpxData))
        {
            trackPoints = ExtractTrackPointsFromJsonData(workout.RawGpxData, "RawGpxData");
        }

        if (trackPoints == null && !string.IsNullOrEmpty(workout.RawFitData))
        {
            trackPoints = ExtractTrackPointsFromJsonData(workout.RawFitData, "RawFitData");
        }

        if (trackPoints == null && !string.IsNullOrEmpty(workout.RawHealthKitData))
        {
            trackPoints = ExtractTrackPointsFromJsonData(workout.RawHealthKitData, "RawHealthKitData");
        }

        if (trackPoints == null && workout.RawFileData != null && workout.RawFileData.Length > 0)
        {
            trackPoints = ReparseTrackPointsFromRawFile(workout);
        }

        if (trackPoints == null && !string.IsNullOrEmpty(workout.Route?.RouteGeoJson))
        {
            _logger.LogInformation("Workout {WorkoutId} using RouteGeoJson, reconstructing timestamps from distance", workout.Id);
            trackPoints = ExtractTrackPointsFromRouteGeoJson(workout.Route.RouteGeoJson);
            if (trackPoints != null && trackPoints.Count > 1)
            {
                trackPoints = ReconstructTimestampsFromDistance(
                    trackPoints, workout.StartedAt, workout.DurationS);
            }
        }

        return trackPoints is { Count: > 0 } ? trackPoints : null;
    }

    private List<TrackPoint>? ExtractTrackPointsFromJsonData(string jsonData, string dataType)
    {
        try
        {
            var rawData = JsonSerializer.Deserialize<JsonElement>(jsonData);
            if (!rawData.TryGetProperty("trackPoints", out var trackPointsElement))
            {
                _logger.LogWarning("{DataType} is missing 'trackPoints' property", dataType);
                return null;
            }

            var trackPoints = new List<TrackPoint>();
            foreach (var pointElement in trackPointsElement.EnumerateArray())
            {
                var point = new TrackPoint();

                var hasLat = pointElement.TryGetProperty("lat", out var latElement) &&
                             latElement.ValueKind == JsonValueKind.Number;
                var hasLon = pointElement.TryGetProperty("lon", out var lonElement) &&
                             lonElement.ValueKind == JsonValueKind.Number;
                if (hasLat && hasLon)
                {
                    point.Latitude = latElement.GetDouble();
                    point.Longitude = lonElement.GetDouble();
                }

                if (pointElement.TryGetProperty("ele", out var eleElement) && eleElement.ValueKind == JsonValueKind.Number)
                {
                    point.Elevation = eleElement.GetDouble();
                }

                if (pointElement.TryGetProperty("time", out var timeElement) && timeElement.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(timeElement.GetString(), null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
                    {
                        point.Time = DateTime.SpecifyKind(time, DateTimeKind.Utc);
                    }
                }
                else if (pointElement.TryGetProperty("t", out var tElement) && tElement.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(tElement.GetString(), null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
                    {
                        point.Time = DateTime.SpecifyKind(t, DateTimeKind.Utc);
                    }
                }

                if (pointElement.TryGetProperty("hr", out var hrElement) && hrElement.ValueKind == JsonValueKind.Number)
                {
                    point.HeartRateBpm = (byte)hrElement.GetInt32();
                }

                if (pointElement.TryGetProperty("cad", out var cadElement) && cadElement.ValueKind == JsonValueKind.Number)
                {
                    point.CadenceRpm = (byte)cadElement.GetInt32();
                }

                if (pointElement.TryGetProperty("power", out var powerElement) && powerElement.ValueKind == JsonValueKind.Number)
                {
                    point.PowerWatts = (ushort)powerElement.GetInt32();
                }
                else if (pointElement.TryGetProperty("pwr", out var pwrElement) && pwrElement.ValueKind == JsonValueKind.Number)
                {
                    point.PowerWatts = (ushort)pwrElement.GetInt32();
                }

                if (pointElement.TryGetProperty("temp", out var tempElement) && tempElement.ValueKind == JsonValueKind.Number)
                {
                    point.TemperatureC = (sbyte)tempElement.GetInt32();
                }

                if (pointElement.TryGetProperty("distM", out var distElement) && distElement.ValueKind == JsonValueKind.Number)
                {
                    point.DistanceM = distElement.GetDouble();
                }

                // Keep GPS points and GPS-free samples that carry a timestamp plus distance or sensors
                // (indoor HealthKit / FIT series). Skip completely empty objects.
                var hasPosition = point.HasPosition;
                var hasTimedPayload = point.Time.HasValue &&
                    (point.DistanceM.HasValue ||
                     point.HeartRateBpm.HasValue ||
                     point.CadenceRpm.HasValue ||
                     point.PowerWatts.HasValue ||
                     point.TemperatureC.HasValue ||
                     point.Elevation.HasValue);
                if (!hasPosition && !hasTimedPayload)
                {
                    continue;
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

    private List<TrackPoint>? ReparseTrackPointsFromRawFile(Workout workout)
    {
        try
        {
            using var stream = new MemoryStream(workout.RawFileData!);

            if (workout.RawFileType == "gpx")
            {
                return _gpxParser.ParseGpx(stream).TrackPoints;
            }

            if (workout.RawFileType == "fit")
            {
                var isGzipped = workout.RawFileName?.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase) == true;
                var parseResult = isGzipped
                    ? _fitParser.ParseGzippedFit(stream)
                    : _fitParser.ParseFit(stream);
                return parseResult.TrackPoints;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-parse track points from RawFileData for workout {WorkoutId}", workout.Id);
        }

        return null;
    }

    private List<TrackPoint>? ExtractTrackPointsFromRouteGeoJson(string routeGeoJson)
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

            var trackPoints = new List<TrackPoint>();
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

                var point = new TrackPoint
                {
                    Longitude = coords[0].GetDouble(),
                    Latitude = coords[1].GetDouble()
                };

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

    private static List<TrackPoint> ReconstructTimestampsFromDistance(
        List<TrackPoint> trackPoints,
        DateTime workoutStartTime,
        int workoutDurationS)
    {
        if (trackPoints.Count < 2)
        {
            return trackPoints;
        }

        var cumulativeDistances = new List<double> { 0.0 };
        double totalDistance = 0.0;

        for (int i = 1; i < trackPoints.Count; i++)
        {
            var prevPoint = trackPoints[i - 1];
            var currPoint = trackPoints[i];
            if (!prevPoint.HasPosition || !currPoint.HasPosition)
            {
                cumulativeDistances.Add(totalDistance);
                continue;
            }

            totalDistance += GeoUtils.HaversineDistance(
                prevPoint.Latitude!.Value, prevPoint.Longitude!.Value,
                currPoint.Latitude!.Value, currPoint.Longitude!.Value);
            cumulativeDistances.Add(totalDistance);
        }

        var reconstructed = new List<TrackPoint>();
        for (int i = 0; i < trackPoints.Count; i++)
        {
            var point = trackPoints[i];
            var distanceRatio = totalDistance > 0 ? cumulativeDistances[i] / totalDistance : 0.0;
            point.Time = workoutStartTime.AddSeconds(distanceRatio * workoutDurationS);
            reconstructed.Add(point);
        }

        return reconstructed;
    }
}
