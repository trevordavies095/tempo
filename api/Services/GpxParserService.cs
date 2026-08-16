using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Tempo.Api.Models;
using Tempo.Api.Utils;

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
        public List<TrackPoint> TrackPoints { get; set; } = new();
        public string? RawGpxDataJson { get; set; }
        public string? Name { get; set; }
    }

    public GpxParseResult ParseGpx(Stream gpxStream)
    {
        var doc = new XmlDocument();
        doc.Load(gpxStream);

        var nsManager = new XmlNamespaceManager(doc.NameTable);
        nsManager.AddNamespace("gpx", "http://www.topografix.com/GPX/1/1");
        nsManager.AddNamespace("gpxtpx", "http://www.garmin.com/xmlschemas/TrackPointExtension/v1");

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

        var trackPoints = new List<TrackPoint>();
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

            var point = new TrackPoint
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

            // Parse TrackPointExtension if present
            var extensionsNode = trkpt.SelectSingleNode("gpx:extensions", nsManager);
            if (extensionsNode != null)
            {
                var tpxNode = extensionsNode.SelectSingleNode("gpxtpx:TrackPointExtension", nsManager);
                if (tpxNode != null)
                {
                    // Parse heart rate (0-255)
                    var hrNode = tpxNode.SelectSingleNode("gpxtpx:hr", nsManager);
                    if (hrNode != null && byte.TryParse(hrNode.InnerText, out var hr) && hr >= 0 && hr <= 255)
                    {
                        point.HeartRateBpm = hr;
                    }

                    // Parse cadence (0-255)
                    var cadNode = tpxNode.SelectSingleNode("gpxtpx:cad", nsManager);
                    if (cadNode != null && byte.TryParse(cadNode.InnerText, out var cad) && cad >= 0 && cad <= 255)
                    {
                        point.CadenceRpm = cad;
                    }

                    // Parse power (0-65535)
                    var powerNode = tpxNode.SelectSingleNode("gpxtpx:power", nsManager);
                    if (powerNode != null && ushort.TryParse(powerNode.InnerText, out var power) && power >= 0 && power <= 65535)
                    {
                        point.PowerWatts = power;
                    }

                    // Parse temperature (-128 to 127, round decimal to integer)
                    var tempNode = tpxNode.SelectSingleNode("gpxtpx:atemp", nsManager);
                    if (tempNode != null)
                    {
                        if (double.TryParse(tempNode.InnerText, out var tempDouble))
                        {
                            var tempInt = (int)Math.Round(tempDouble);
                            if (tempInt >= sbyte.MinValue && tempInt <= sbyte.MaxValue)
                            {
                                point.TemperatureC = (sbyte)tempInt;
                            }
                        }
                    }
                }
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
            totalDistance += GeoUtils.HaversineDistance(
                trackPoints[i - 1].Latitude!.Value,
                trackPoints[i - 1].Longitude!.Value,
                trackPoints[i].Latitude!.Value,
                trackPoints[i].Longitude!.Value
            );
        }

        // Calculate elevation gain with noise filtering
        double? elevationGain = CalculateElevationChange(trackPoints, calculateGain: true);

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
            extensions = new Dictionary<string, object>(),
            trackPoints = trackPoints.Select(p => new
            {
                lat = p.Latitude,
                lon = p.Longitude,
                ele = p.Elevation,
                time = p.Time?.ToString("O"),
                hr = p.HeartRateBpm,
                cad = p.CadenceRpm,
                power = p.PowerWatts,
                temp = p.TemperatureC
            }).ToList(),
            calculated = calculated,
            source = "gpx_import",
            importedAt = DateTime.UtcNow.ToString("O")
        };

        var rawGpxDataJson = JsonSerializer.Serialize(rawGpxData, JsonUtils.DefaultOptions);

        string? name = null;
        if (metadata.TryGetValue("name", out var nameObj) && nameObj is string nameStr && !string.IsNullOrWhiteSpace(nameStr))
        {
            name = nameStr;
        }

        return new GpxParseResult
        {
            StartTime = DateTime.SpecifyKind(startTime.Value, DateTimeKind.Utc),
            DurationSeconds = duration,
            DistanceMeters = totalDistance,
            TrackPoints = trackPoints,
            RawGpxDataJson = rawGpxDataJson,
            Name = name
        };
    }

    /// <summary>
    /// Calculates elevation change (gain or loss) with noise filtering.
    /// </summary>
    /// <param name="trackPoints">List of track points</param>
    /// <param name="calculateGain">If true, calculates elevation gain; if false, calculates elevation loss</param>
    /// <returns>Elevation change in meters. For gain: null if no gain; for loss: 0.0 if no loss</returns>
    private double? CalculateElevationChange(List<TrackPoint> trackPoints, bool calculateGain)
    {
        if (!trackPoints.Any(p => p.Elevation.HasValue))
        {
            return calculateGain ? null : 0.0;
        }

        double totalChange = 0.0;
        double accumulatedChange = 0.0; // The direction we're tracking (gain or loss)
        double accumulatedOpposite = 0.0; // The opposite direction
        double accumulatedDistance = 0.0;
        double? lastElevation = null;
        TrackPoint? lastPoint = null;

        foreach (var point in trackPoints)
        {
            if (!point.Elevation.HasValue)
            {
                // Skip points without elevation, but continue tracking distance
                if (lastPoint != null)
                {
                    accumulatedDistance += GeoUtils.HaversineDistance(
                        lastPoint.Latitude!.Value,
                        lastPoint.Longitude!.Value,
                        point.Latitude!.Value,
                        point.Longitude!.Value
                    );
                }
                lastPoint = point;
                continue;
            }

            double currentElevation = point.Elevation.Value;

            if (lastElevation.HasValue && lastPoint != null)
            {
                // Calculate horizontal distance since last point
                double segmentDistance = GeoUtils.HaversineDistance(
                    lastPoint.Latitude!.Value,
                    lastPoint.Longitude!.Value,
                    point.Latitude!.Value,
                    point.Longitude!.Value
                );
                accumulatedDistance += segmentDistance;

                // Calculate elevation change
                double elevationDiff = currentElevation - lastElevation.Value;

                if (calculateGain)
                {
                    // Calculate elevation gain
                    if (elevationDiff > 0)
                    {
                        // Gaining elevation
                        if (accumulatedOpposite > 0)
                        {
                            // Direction changed from loss to gain
                            // Process accumulated loss (we don't count loss, but reset it)
                            accumulatedOpposite = 0.0;
                            accumulatedDistance = 0.0;
                        }
                        accumulatedChange += elevationDiff;
                    }
                    else if (elevationDiff < 0)
                    {
                        // Losing elevation
                        if (accumulatedChange > 0)
                        {
                            // Direction changed from gain to loss
                            // Check if accumulated gain should be counted
                            if (accumulatedChange >= _elevationConfig.NoiseThresholdMeters &&
                                accumulatedDistance >= _elevationConfig.MinDistanceMeters)
                            {
                                totalChange += accumulatedChange;
                            }
                            // Reset accumulators
                            accumulatedChange = 0.0;
                            accumulatedDistance = 0.0;
                        }
                        accumulatedOpposite += Math.Abs(elevationDiff);
                    }
                }
                else
                {
                    // Calculate elevation loss
                    if (elevationDiff < 0)
                    {
                        // Losing elevation
                        if (accumulatedOpposite > 0)
                        {
                            // Direction changed from gain to loss
                            // Reset gain accumulator
                            accumulatedOpposite = 0.0;
                            accumulatedDistance = 0.0;
                        }
                        accumulatedChange += Math.Abs(elevationDiff);
                    }
                    else if (elevationDiff > 0)
                    {
                        // Gaining elevation
                        if (accumulatedChange > 0)
                        {
                            // Direction changed from loss to gain
                            // Check if accumulated loss should be counted
                            if (accumulatedChange >= _elevationConfig.NoiseThresholdMeters &&
                                accumulatedDistance >= _elevationConfig.MinDistanceMeters)
                            {
                                totalChange += accumulatedChange;
                            }
                            // Reset accumulators
                            accumulatedChange = 0.0;
                            accumulatedDistance = 0.0;
                        }
                        accumulatedOpposite += elevationDiff;
                    }
                }
                // If elevationDiff == 0, we continue accumulating distance but don't change elevation accumulators
            }

            lastElevation = currentElevation;
            lastPoint = point;
        }

        // Process any remaining accumulated change at the end
        if (accumulatedChange > 0)
        {
            if (accumulatedChange >= _elevationConfig.NoiseThresholdMeters &&
                accumulatedDistance >= _elevationConfig.MinDistanceMeters)
            {
                totalChange += accumulatedChange;
            }
        }

        if (calculateGain)
        {
            return totalChange > 0 ? totalChange : null;
        }
        else
        {
            return totalChange;
        }
    }

    private Dictionary<string, object> CalculateAdditionalMetrics(List<TrackPoint> trackPoints, double totalDistance, int duration, double? elevationGain)
    {
        var calculated = new Dictionary<string, object>();

        // Calculate max speed, min/max elevation, elevation loss
        double? minElev = null;
        double? maxElev = null;
        double elevationLoss = CalculateElevationChange(trackPoints, calculateGain: false) ?? 0.0;
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
                    var segmentDistance = GeoUtils.HaversineDistance(
                        trackPoints[i - 1].Latitude!.Value,
                        trackPoints[i - 1].Longitude!.Value,
                        point.Latitude!.Value,
                        point.Longitude!.Value
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
}

