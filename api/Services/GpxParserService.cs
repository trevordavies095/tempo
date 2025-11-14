using System.Globalization;
using System.Text.Json;
using System.Xml;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public class GpxParserService
{
    private readonly ElevationCalculationConfig _elevationConfig;

    public GpxParserService(ElevationCalculationConfig elevationConfig)
    {
        _elevationConfig = elevationConfig;
    }
    public class GpxParseResult
    {
        public DateTime StartTime { get; set; }
        public int DurationSeconds { get; set; }
        public double DistanceMeters { get; set; }
        public double? ElevationGainMeters { get; set; }
        public List<GpxPoint> TrackPoints { get; set; } = new();
        public string? RawGpxDataJson { get; set; }  // JSON string for RawGpxData field
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

        // Extract metadata
        var metadata = new Dictionary<string, object?>();
        var metadataNode = doc.SelectSingleNode("//gpx:metadata", nsManager);
        if (metadataNode != null)
        {
            var nameNode = metadataNode.SelectSingleNode("gpx:name", nsManager);
            if (nameNode != null) metadata["name"] = nameNode.InnerText;

            var descNode = metadataNode.SelectSingleNode("gpx:desc", nsManager);
            if (descNode != null) metadata["desc"] = descNode.InnerText;

            var authorNode = metadataNode.SelectSingleNode("gpx:author", nsManager);
            if (authorNode != null)
            {
                var authorName = authorNode.SelectSingleNode("gpx:name", nsManager);
                if (authorName != null) metadata["author"] = authorName.InnerText;
            }

            var timeNode = metadataNode.SelectSingleNode("gpx:time", nsManager);
            if (timeNode != null && DateTime.TryParse(timeNode.InnerText, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var metaTime))
            {
                metadata["time"] = DateTime.SpecifyKind(metaTime, DateTimeKind.Utc).ToString("O");
            }

            var keywordsNode = metadataNode.SelectSingleNode("gpx:keywords", nsManager);
            if (keywordsNode != null) metadata["keywords"] = keywordsNode.InnerText;
        }

        // Extract track metadata
        var trackNode = doc.SelectSingleNode("//gpx:trk", nsManager);
        if (trackNode != null)
        {
            var trackNameNode = trackNode.SelectSingleNode("gpx:name", nsManager);
            if (trackNameNode != null && !metadata.ContainsKey("name"))
            {
                metadata["name"] = trackNameNode.InnerText;
            }

            var trackDescNode = trackNode.SelectSingleNode("gpx:desc", nsManager);
            if (trackDescNode != null && !metadata.ContainsKey("desc"))
            {
                metadata["desc"] = trackDescNode.InnerText;
            }
        }

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

        // Calculate elevation gain with noise filtering
        double? elevationGain = CalculateElevationGain(trackPoints);

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

        // Calculate additional metrics
        var calculated = CalculateAdditionalMetrics(trackPoints, totalDistance, duration, elevationGain);

        // Build RawGpxData JSON
        var rawGpxData = new
        {
            metadata = metadata.Count > 0 ? metadata : null,
            extensions = new Dictionary<string, object>(), // TODO: Extract extensions if needed
            trackPoints = trackPoints.Select(p => new
            {
                lat = p.Latitude,
                lon = p.Longitude,
                ele = p.Elevation,
                time = p.Time?.ToString("O")
            }).ToList(),
            calculated = calculated,
            source = "gpx_import",
            importedAt = DateTime.UtcNow.ToString("O")
        };

        var rawGpxDataJson = JsonSerializer.Serialize(rawGpxData, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        return new GpxParseResult
        {
            StartTime = DateTime.SpecifyKind(startTime.Value, DateTimeKind.Utc),
            DurationSeconds = duration,
            DistanceMeters = totalDistance,
            ElevationGainMeters = elevationGain,
            TrackPoints = trackPoints,
            RawGpxDataJson = rawGpxDataJson
        };
    }

    private double? CalculateElevationGain(List<GpxPoint> trackPoints)
    {
        if (!trackPoints.Any(p => p.Elevation.HasValue))
        {
            return null;
        }

        double totalElevationGain = 0.0;
        double accumulatedElevationGain = 0.0;
        double accumulatedElevationLoss = 0.0;
        double accumulatedDistance = 0.0;
        double? lastElevation = null;
        GpxPoint? lastPoint = null;

        foreach (var point in trackPoints)
        {
            if (!point.Elevation.HasValue)
            {
                // Skip points without elevation, but continue tracking distance
                if (lastPoint != null)
                {
                    accumulatedDistance += HaversineDistance(
                        lastPoint.Latitude,
                        lastPoint.Longitude,
                        point.Latitude,
                        point.Longitude
                    );
                }
                lastPoint = point;
                continue;
            }

            double currentElevation = point.Elevation.Value;

            if (lastElevation.HasValue && lastPoint != null)
            {
                // Calculate horizontal distance since last point
                double segmentDistance = HaversineDistance(
                    lastPoint.Latitude,
                    lastPoint.Longitude,
                    point.Latitude,
                    point.Longitude
                );
                accumulatedDistance += segmentDistance;

                // Calculate elevation change
                double elevationDiff = currentElevation - lastElevation.Value;

                if (elevationDiff > 0)
                {
                    // Gaining elevation
                    if (accumulatedElevationLoss > 0)
                    {
                        // Direction changed from loss to gain
                        // Process accumulated loss (we don't count loss, but reset it)
                        accumulatedElevationLoss = 0.0;
                        accumulatedDistance = 0.0;
                    }
                    accumulatedElevationGain += elevationDiff;
                }
                else if (elevationDiff < 0)
                {
                    // Losing elevation
                    if (accumulatedElevationGain > 0)
                    {
                        // Direction changed from gain to loss
                        // Check if accumulated gain should be counted
                        if (accumulatedElevationGain >= _elevationConfig.NoiseThresholdMeters &&
                            accumulatedDistance >= _elevationConfig.MinDistanceMeters)
                        {
                            totalElevationGain += accumulatedElevationGain;
                        }
                        // Reset accumulators
                        accumulatedElevationGain = 0.0;
                        accumulatedDistance = 0.0;
                    }
                    accumulatedElevationLoss += Math.Abs(elevationDiff);
                }
                // If elevationDiff == 0, we continue accumulating distance but don't change elevation accumulators
            }

            lastElevation = currentElevation;
            lastPoint = point;
        }

        // Process any remaining accumulated elevation gain at the end
        if (accumulatedElevationGain > 0)
        {
            if (accumulatedElevationGain >= _elevationConfig.NoiseThresholdMeters &&
                accumulatedDistance >= _elevationConfig.MinDistanceMeters)
            {
                totalElevationGain += accumulatedElevationGain;
            }
        }

        return totalElevationGain > 0 ? totalElevationGain : null;
    }

    private double CalculateElevationLoss(List<GpxPoint> trackPoints)
    {
        if (!trackPoints.Any(p => p.Elevation.HasValue))
        {
            return 0.0;
        }

        double totalElevationLoss = 0.0;
        double accumulatedElevationGain = 0.0;
        double accumulatedElevationLoss = 0.0;
        double accumulatedDistance = 0.0;
        double? lastElevation = null;
        GpxPoint? lastPoint = null;

        foreach (var point in trackPoints)
        {
            if (!point.Elevation.HasValue)
            {
                // Skip points without elevation, but continue tracking distance
                if (lastPoint != null)
                {
                    accumulatedDistance += HaversineDistance(
                        lastPoint.Latitude,
                        lastPoint.Longitude,
                        point.Latitude,
                        point.Longitude
                    );
                }
                lastPoint = point;
                continue;
            }

            double currentElevation = point.Elevation.Value;

            if (lastElevation.HasValue && lastPoint != null)
            {
                // Calculate horizontal distance since last point
                double segmentDistance = HaversineDistance(
                    lastPoint.Latitude,
                    lastPoint.Longitude,
                    point.Latitude,
                    point.Longitude
                );
                accumulatedDistance += segmentDistance;

                // Calculate elevation change
                double elevationDiff = currentElevation - lastElevation.Value;

                if (elevationDiff < 0)
                {
                    // Losing elevation
                    if (accumulatedElevationGain > 0)
                    {
                        // Direction changed from gain to loss
                        // Reset gain accumulator
                        accumulatedElevationGain = 0.0;
                        accumulatedDistance = 0.0;
                    }
                    accumulatedElevationLoss += Math.Abs(elevationDiff);
                }
                else if (elevationDiff > 0)
                {
                    // Gaining elevation
                    if (accumulatedElevationLoss > 0)
                    {
                        // Direction changed from loss to gain
                        // Check if accumulated loss should be counted
                        if (accumulatedElevationLoss >= _elevationConfig.NoiseThresholdMeters &&
                            accumulatedDistance >= _elevationConfig.MinDistanceMeters)
                        {
                            totalElevationLoss += accumulatedElevationLoss;
                        }
                        // Reset accumulators
                        accumulatedElevationLoss = 0.0;
                        accumulatedDistance = 0.0;
                    }
                    accumulatedElevationGain += elevationDiff;
                }
                // If elevationDiff == 0, we continue accumulating distance but don't change elevation accumulators
            }

            lastElevation = currentElevation;
            lastPoint = point;
        }

        // Process any remaining accumulated elevation loss at the end
        if (accumulatedElevationLoss > 0)
        {
            if (accumulatedElevationLoss >= _elevationConfig.NoiseThresholdMeters &&
                accumulatedDistance >= _elevationConfig.MinDistanceMeters)
            {
                totalElevationLoss += accumulatedElevationLoss;
            }
        }

        return totalElevationLoss;
    }

    private Dictionary<string, object> CalculateAdditionalMetrics(List<GpxPoint> trackPoints, double totalDistance, int duration, double? elevationGain)
    {
        var calculated = new Dictionary<string, object>();

        // Calculate max speed, min/max elevation, elevation loss
        double? minElev = null;
        double? maxElev = null;
        double elevationLoss = CalculateElevationLoss(trackPoints);
        double maxSpeedMps = 0.0;
        double totalGrade = 0.0;
        double maxPosGrade = 0.0;
        double maxNegGrade = 0.0;
        double? minLat = null, maxLat = null, minLon = null, maxLon = null;

        double? lastElevation = null;
        for (int i = 0; i < trackPoints.Count; i++)
        {
            var point = trackPoints[i];

            // Track bounds
            if (minLat == null || point.Latitude < minLat) minLat = point.Latitude;
            if (maxLat == null || point.Latitude > maxLat) maxLat = point.Latitude;
            if (minLon == null || point.Longitude < minLon) minLon = point.Longitude;
            if (maxLon == null || point.Longitude > maxLon) maxLon = point.Longitude;

            // Track elevation
            if (point.Elevation.HasValue)
            {
                if (minElev == null || point.Elevation.Value < minElev) minElev = point.Elevation.Value;
                if (maxElev == null || point.Elevation.Value > maxElev) maxElev = point.Elevation.Value;
                lastElevation = point.Elevation.Value;
            }

            // Calculate speed and grade between consecutive points
            if (i > 0 && point.Time.HasValue && trackPoints[i - 1].Time.HasValue)
            {
                var timeDiff = (point.Time.Value - trackPoints[i - 1].Time.Value).TotalSeconds;
                if (timeDiff > 0)
                {
                    var segmentDistance = HaversineDistance(
                        trackPoints[i - 1].Latitude,
                        trackPoints[i - 1].Longitude,
                        point.Latitude,
                        point.Longitude
                    );
                    var speed = segmentDistance / timeDiff;
                    if (speed > maxSpeedMps) maxSpeedMps = speed;

                    // Calculate grade
                    if (point.Elevation.HasValue && trackPoints[i - 1].Elevation.HasValue && segmentDistance > 0)
                    {
                        var elevDiff = point.Elevation.Value - trackPoints[i - 1].Elevation.Value;
                        var grade = (elevDiff / segmentDistance) * 100.0;
                        totalGrade += grade;
                        if (grade > maxPosGrade) maxPosGrade = grade;
                        if (grade < maxNegGrade) maxNegGrade = grade;
                    }
                }
            }
        }

        if (minElev.HasValue) calculated["minElevM"] = minElev.Value;
        if (maxElev.HasValue) calculated["maxElevM"] = maxElev.Value;
        if (elevationLoss > 0) calculated["elevLossM"] = elevationLoss;
        if (maxSpeedMps > 0) calculated["maxSpeedMps"] = maxSpeedMps;
        if (totalDistance > 0 && trackPoints.Count > 1)
        {
            calculated["avgSpeedMps"] = totalDistance / duration;
            calculated["avgGradePercent"] = totalGrade / (trackPoints.Count - 1);
        }
        if (maxPosGrade > 0) calculated["maxPosGradePercent"] = maxPosGrade;
        if (maxNegGrade < 0) calculated["maxNegGradePercent"] = maxNegGrade;

        if (minLat.HasValue && maxLat.HasValue && minLon.HasValue && maxLon.HasValue)
        {
            calculated["routeBounds"] = new
            {
                minLat = minLat.Value,
                maxLat = maxLat.Value,
                minLon = minLon.Value,
                maxLon = maxLon.Value
            };
        }

        return calculated;
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
        var splitStartDistance = 0.0;
        var splitStartIndex = 0;
        var lastSplitStartIndex = 0; // Track start index of the last created split
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

            if (accumulatedDistance - splitStartDistance >= splitDistanceMeters)
            {
                // Calculate the actual split distance (not accumulated)
                var splitDistance = accumulatedDistance - splitStartDistance;

                // Calculate time for this split
                var splitDuration = 0;
                if (trackPoints[i].Time.HasValue && trackPoints[splitStartIndex].Time.HasValue)
                {
                    splitDuration = (int)(trackPoints[i].Time!.Value - trackPoints[splitStartIndex].Time!.Value).TotalSeconds;
                }
                else
                {
                    // Estimate based on proportion of total distance
                    splitDuration = (int)((splitDistance / distanceMeters) * durationSeconds);
                }

                // Calculate split pace in seconds per km (stored in metric)
                var splitPace = splitDuration > 0 ? (int)(splitDuration / (splitDistance / 1000.0)) : 0;

                splits.Add(new WorkoutSplit
                {
                    Id = Guid.NewGuid(),
                    Idx = splitIndex++,
                    DistanceM = splitDistance, // Store actual split distance, not accumulated
                    DurationS = splitDuration,
                    PaceS = splitPace
                });

                // Reset for next split
                splitStartDistance = accumulatedDistance;
                lastSplitStartIndex = splitStartIndex; // Store the start index of the split we just created
                splitStartIndex = i;
            }
        }

        // Handle final partial split
        var remainingDistance = accumulatedDistance - splitStartDistance;
        if (remainingDistance > 0)
        {
            // Create separate split if significant (>10% of split distance), otherwise merge into last split
            if (remainingDistance >= splitDistanceMeters * 0.1 && splits.Count > 0)
            {
                // Calculate time for final partial split
                var finalSplitDuration = 0;
                if (trackPoints.Count > 1 && trackPoints[trackPoints.Count - 1].Time.HasValue && trackPoints[splitStartIndex].Time.HasValue)
                {
                    finalSplitDuration = (int)(trackPoints[trackPoints.Count - 1].Time!.Value - trackPoints[splitStartIndex].Time!.Value).TotalSeconds;
                }
                else
                {
                    // Estimate based on proportion
                    finalSplitDuration = (int)((remainingDistance / distanceMeters) * durationSeconds);
                }

                var finalSplitPace = finalSplitDuration > 0 ? (int)(finalSplitDuration / (remainingDistance / 1000.0)) : 0;

                splits.Add(new WorkoutSplit
                {
                    Id = Guid.NewGuid(),
                    Idx = splitIndex,
                    DistanceM = remainingDistance,
                    DurationS = finalSplitDuration,
                    PaceS = finalSplitPace
                });
            }
            else if (splits.Count > 0)
            {
                // Merge small remainder into last split
                var lastSplit = splits.Last();
                var totalLastSplitDistance = lastSplit.DistanceM + remainingDistance;
                
                // Recalculate duration and pace for merged split
                var mergedDuration = lastSplit.DurationS;
                if (trackPoints.Count > 1 && trackPoints[trackPoints.Count - 1].Time.HasValue)
                {
                    // Use the start time of the last split (stored in lastSplitStartIndex)
                    var lastSplitStartTime = trackPoints[lastSplitStartIndex].Time;
                    if (lastSplitStartTime.HasValue && trackPoints[trackPoints.Count - 1].Time.HasValue)
                    {
                        mergedDuration = (int)(trackPoints[trackPoints.Count - 1].Time!.Value - lastSplitStartTime.Value).TotalSeconds;
                    }
                }
                else
                {
                    // Estimate based on proportion of total distance for the merged split
                    mergedDuration = (int)((totalLastSplitDistance / distanceMeters) * durationSeconds);
                }

                lastSplit.DistanceM = totalLastSplitDistance;
                lastSplit.DurationS = mergedDuration;
                lastSplit.PaceS = mergedDuration > 0 ? (int)(mergedDuration / (totalLastSplitDistance / 1000.0)) : lastSplit.PaceS;
            }
        }

        return splits;
    }
}

