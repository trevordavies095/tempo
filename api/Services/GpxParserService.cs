using System.Globalization;
using System.Xml;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public class GpxParserService
{
    public class GpxParseResult
    {
        public DateTime StartTime { get; set; }
        public int DurationSeconds { get; set; }
        public double DistanceMeters { get; set; }
        public double? ElevationGainMeters { get; set; }
        public List<GpxPoint> TrackPoints { get; set; } = new();
    }

    public class GpxPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Elevation { get; set; }
        public DateTime? Time { get; set; }
    }

    public GpxParseResult ParseGpx(Stream gpxStream)
    {
        var doc = new XmlDocument();
        doc.Load(gpxStream);

        var nsManager = new XmlNamespaceManager(doc.NameTable);
        nsManager.AddNamespace("gpx", "http://www.topografix.com/GPX/1/1");

        var trackPoints = new List<GpxPoint>();
        var startTime = (DateTime?)null;
        var endTime = (DateTime?)null;

        // Find all track points
        var trkptNodes = doc.SelectNodes("//gpx:trkpt", nsManager);
        if (trkptNodes == null || trkptNodes.Count == 0)
        {
            throw new InvalidOperationException("No track points found in GPX file");
        }

        foreach (XmlNode? trkpt in trkptNodes)
        {
            if (trkpt?.Attributes == null) continue;

            var latAttr = trkpt.Attributes["lat"];
            var lonAttr = trkpt.Attributes["lon"];

            if (latAttr == null || lonAttr == null) continue;

            if (!double.TryParse(latAttr.Value, out var lat) ||
                !double.TryParse(lonAttr.Value, out var lon))
                continue;

            var point = new GpxPoint
            {
                Latitude = lat,
                Longitude = lon
            };

            // Get elevation if present
            var eleNode = trkpt.SelectSingleNode("gpx:ele", nsManager);
            if (eleNode != null && double.TryParse(eleNode.InnerText, out var ele))
            {
                point.Elevation = ele;
            }

            // Get time if present
            var timeNode = trkpt.SelectSingleNode("gpx:time", nsManager);
            if (timeNode != null && DateTime.TryParse(timeNode.InnerText, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            {
                // Ensure the DateTime is marked as UTC
                var utcTime = DateTime.SpecifyKind(time, DateTimeKind.Utc);
                point.Time = utcTime;
                if (startTime == null)
                    startTime = utcTime;
                endTime = utcTime;
            }

            trackPoints.Add(point);
        }

        if (trackPoints.Count < 2)
        {
            throw new InvalidOperationException("GPX file must contain at least 2 track points");
        }

        // Calculate distance using Haversine formula
        var totalDistance = 0.0;
        for (int i = 1; i < trackPoints.Count; i++)
        {
            totalDistance += HaversineDistance(
                trackPoints[i - 1].Latitude,
                trackPoints[i - 1].Longitude,
                trackPoints[i].Latitude,
                trackPoints[i].Longitude
            );
        }

        // Calculate elevation gain
        double? elevationGain = null;
        if (trackPoints.Any(p => p.Elevation.HasValue))
        {
            elevationGain = 0.0;
            double? lastElevation = null;
            foreach (var point in trackPoints)
            {
                if (point.Elevation.HasValue && lastElevation.HasValue)
                {
                    var diff = point.Elevation.Value - lastElevation.Value;
                    if (diff > 0)
                        elevationGain += diff;
                }
                if (point.Elevation.HasValue)
                    lastElevation = point.Elevation.Value;
            }
        }

        // Calculate duration
        var duration = 0;
        if (startTime.HasValue && endTime.HasValue)
        {
            duration = (int)(endTime.Value - startTime.Value).TotalSeconds;
        }

        if (startTime == null)
        {
            throw new InvalidOperationException("GPX file must contain timestamps");
        }

        return new GpxParseResult
        {
            StartTime = DateTime.SpecifyKind(startTime.Value, DateTimeKind.Utc),
            DurationSeconds = duration,
            DistanceMeters = totalDistance,
            ElevationGainMeters = elevationGain,
            TrackPoints = trackPoints
        };
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    public List<WorkoutSplit> CalculateSplits(List<GpxPoint> trackPoints, double distanceMeters, int durationSeconds, double splitDistanceMeters = 1000.0)
    {
        var splits = new List<WorkoutSplit>();
        var accumulatedDistance = 0.0;
        var splitStartIndex = 0;
        var splitIndex = 0;

        for (int i = 1; i < trackPoints.Count; i++)
        {
            var segmentDistance = HaversineDistance(
                trackPoints[i - 1].Latitude,
                trackPoints[i - 1].Longitude,
                trackPoints[i].Latitude,
                trackPoints[i].Longitude
            );

            accumulatedDistance += segmentDistance;

            if (accumulatedDistance >= splitDistanceMeters)
            {
                // Calculate time for this split
                var splitDuration = 0;
                if (trackPoints[i].Time.HasValue && trackPoints[splitStartIndex].Time.HasValue)
                {
                    splitDuration = (int)(trackPoints[i].Time!.Value - trackPoints[splitStartIndex].Time!.Value).TotalSeconds;
                }
                else
                {
                    // Estimate based on proportion of total distance
                    splitDuration = (int)((accumulatedDistance / distanceMeters) * durationSeconds);
                }

                var splitPace = splitDuration > 0 ? (int)(splitDuration / (accumulatedDistance / 1000.0)) : 0;

                splits.Add(new WorkoutSplit
                {
                    Id = Guid.NewGuid(),
                    Idx = splitIndex++,
                    DistanceM = accumulatedDistance,
                    DurationS = splitDuration,
                    PaceS = splitPace
                });

                accumulatedDistance = 0.0;
                splitStartIndex = i;
            }
        }

        // Add final partial split if there's remaining distance
        if (accumulatedDistance > 0 && splits.Count > 0)
        {
            var lastSplit = splits.Last();
            lastSplit.DistanceM += accumulatedDistance;
        }

        return splits;
    }
}

