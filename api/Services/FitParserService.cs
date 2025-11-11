using System.IO.Compression;
using Tempo.Api.Services;
using Dynastream.Fit;

namespace Tempo.Api.Services;

public class FitParserService
{
    // Conversion factor: 180 degrees / 2^31 semicircles
    private const double SemicirclesToDegrees = 180.0 / 2147483648.0;

    public class FitParseResult
    {
        public System.DateTime StartTime { get; set; }
        public int DurationSeconds { get; set; }
        public double DistanceMeters { get; set; }
        public double? ElevationGainMeters { get; set; }
        public List<GpxParserService.GpxPoint> TrackPoints { get; set; } = new();
    }

    public FitParseResult ParseFit(Stream fitStream)
    {
        try
        {
            // Create Decode object
            Decode decoder = new Decode();

            // Check if it's a FIT file
            if (!decoder.IsFIT(fitStream))
            {
                throw new InvalidOperationException("Not a valid FIT file");
            }

            // Use FitListener to collect messages
            FitListener fitListener = new FitListener();
            decoder.MesgEvent += fitListener.OnMesg;

            // Decode the file
            decoder.Read(fitStream);

            // Access messages
            FitMessages messages = fitListener.FitMessages;
            var records = messages.RecordMesgs;
            var sessions = messages.SessionMesgs;

            // Extract data from SessionMesg if available (preferred source for summary data)
            SessionMesg? session = sessions.FirstOrDefault();
            System.DateTime? startTime = null;
            double totalDistance = 0.0;
            float? totalElapsedTime = null;

            if (session != null)
            {
                startTime = session.GetStartTime()?.GetDateTime();
                totalDistance = session.GetTotalDistance() ?? 0.0;
                totalElapsedTime = session.GetTotalElapsedTime();
            }

            // Extract track points from RecordMesg
            var trackPoints = new List<GpxParserService.GpxPoint>();
            System.DateTime? firstTimestamp = null;
            System.DateTime? lastTimestamp = null;
            double? lastElevation = null;
            double elevationGain = 0.0;
            double? lastDistance = null;

            foreach (var record in records)
            {
                var timestamp = record.GetTimestamp()?.GetDateTime().ToUniversalTime();
                if (timestamp == null)
                {
                    continue; // Skip records without timestamps
                }

                if (firstTimestamp == null)
                {
                    firstTimestamp = timestamp;
                }
                lastTimestamp = timestamp;

                // Get position (in semicircles)
                var positionLat = record.GetPositionLat();
                var positionLong = record.GetPositionLong();

                // Get altitude (prefer enhanced altitude if available)
                var altitude = record.GetEnhancedAltitude() ?? record.GetAltitude();

                // Get distance (cumulative)
                var distance = record.GetDistance();

                // Convert semicircles to degrees
                double? latitude = null;
                double? longitude = null;

                if (positionLat.HasValue)
                {
                    latitude = positionLat.Value * SemicirclesToDegrees;
                }

                if (positionLong.HasValue)
                {
                    longitude = positionLong.Value * SemicirclesToDegrees;
                }

                // Only add track points if we have valid position data
                if (latitude.HasValue && longitude.HasValue)
                {
                    var point = new GpxParserService.GpxPoint
                    {
                        Latitude = latitude.Value,
                        Longitude = longitude.Value,
                        Time = timestamp.Value,
                        Elevation = altitude
                    };

                    trackPoints.Add(point);
                }

                // Calculate elevation gain
                if (altitude.HasValue && lastElevation.HasValue)
                {
                    var diff = altitude.Value - lastElevation.Value;
                    if (diff > 0)
                    {
                        elevationGain += diff;
                    }
                }
                if (altitude.HasValue)
                {
                    lastElevation = altitude.Value;
                }

                // Track last distance for fallback calculation
                if (distance.HasValue)
                {
                    lastDistance = distance.Value;
                }
            }

            // Determine start time
            if (startTime == null)
            {
                if (firstTimestamp == null)
                {
                    throw new InvalidOperationException("FIT file must contain timestamps");
                }
                startTime = firstTimestamp;
            }

            // Determine total distance
            if (totalDistance == 0.0 && lastDistance.HasValue)
            {
                totalDistance = lastDistance.Value;
            }

            // Determine duration
            int durationSeconds = 0;
            if (totalElapsedTime.HasValue)
            {
                durationSeconds = (int)Math.Round(totalElapsedTime.Value);
            }
            else if (firstTimestamp.HasValue && lastTimestamp.HasValue)
            {
                durationSeconds = (int)(lastTimestamp.Value - firstTimestamp.Value).TotalSeconds;
            }

            // Handle files with no GPS data (indoor activities)
            // If we have no track points but have session data, that's okay
            if (trackPoints.Count == 0 && totalDistance == 0.0)
            {
                throw new InvalidOperationException("FIT file contains no GPS data and no distance information");
            }

            return new FitParseResult
            {
                StartTime = System.DateTime.SpecifyKind(startTime.Value, System.DateTimeKind.Utc),
                DurationSeconds = durationSeconds,
                DistanceMeters = totalDistance,
                ElevationGainMeters = trackPoints.Count > 0 && elevationGain > 0 ? elevationGain : null,
                TrackPoints = trackPoints
            };
        }
        catch (FitException ex)
        {
            throw new InvalidOperationException($"Error parsing FIT file: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Unexpected error parsing FIT file: {ex.Message}", ex);
        }
    }

    public FitParseResult ParseGzippedFit(Stream gzippedStream)
    {
        using var gzipStream = new GZipStream(gzippedStream, CompressionMode.Decompress);
        using var memoryStream = new MemoryStream();
        gzipStream.CopyTo(memoryStream);
        memoryStream.Position = 0; // Reset to beginning for parsing
        return ParseFit(memoryStream);
    }
}

