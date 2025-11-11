using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.Json;
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
        public string? RawFitDataJson { get; set; }  // JSON string for RawFitData field
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
            var deviceInfos = messages.DeviceInfoMesgs;

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

            // Build RawFitData JSON
            var rawFitData = BuildRawFitData(session, deviceInfos, records.Count);

            return new FitParseResult
            {
                StartTime = System.DateTime.SpecifyKind(startTime.Value, System.DateTimeKind.Utc),
                DurationSeconds = durationSeconds,
                DistanceMeters = totalDistance,
                ElevationGainMeters = trackPoints.Count > 0 && elevationGain > 0 ? elevationGain : null,
                TrackPoints = trackPoints,
                RawFitDataJson = rawFitData
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

    private string? BuildRawFitData(SessionMesg? session, ReadOnlyCollection<DeviceInfoMesg> deviceInfos, int recordCount)
    {
        if (session == null)
        {
            return null;
        }

        var sessionData = new Dictionary<string, object?>();

        // Extract all SessionMesg fields
        if (session.GetTotalElapsedTime().HasValue)
            sessionData["totalElapsedTime"] = session.GetTotalElapsedTime().Value;
        if (session.GetTotalTimerTime().HasValue)
            sessionData["totalTimerTime"] = session.GetTotalTimerTime().Value;
        if (session.GetTotalMovingTime().HasValue)
            sessionData["totalMovingTime"] = session.GetTotalMovingTime().Value;
        if (session.GetTotalDistance().HasValue)
            sessionData["totalDistance"] = session.GetTotalDistance().Value;
        if (session.GetTotalCycles().HasValue)
            sessionData["totalCycles"] = session.GetTotalCycles().Value;
        if (session.GetTotalStrides().HasValue)
            sessionData["totalStrides"] = session.GetTotalStrides().Value;
        if (session.GetTotalStrokes().HasValue)
            sessionData["totalStrokes"] = session.GetTotalStrokes().Value;
        if (session.GetTotalCalories().HasValue)
            sessionData["totalCalories"] = session.GetTotalCalories().Value;
        if (session.GetTotalFatCalories().HasValue)
            sessionData["totalFatCalories"] = session.GetTotalFatCalories().Value;
        if (session.GetMaxSpeed().HasValue)
            sessionData["maxSpeed"] = session.GetMaxSpeed().Value;
        if (session.GetAvgSpeed().HasValue)
            sessionData["avgSpeed"] = session.GetAvgSpeed().Value;
        if (session.GetMaxHeartRate().HasValue)
            sessionData["maxHeartRate"] = session.GetMaxHeartRate().Value;
        if (session.GetAvgHeartRate().HasValue)
            sessionData["avgHeartRate"] = session.GetAvgHeartRate().Value;
        if (session.GetMinHeartRate().HasValue)
            sessionData["minHeartRate"] = session.GetMinHeartRate().Value;
        if (session.GetMaxCadence().HasValue)
            sessionData["maxCadence"] = session.GetMaxCadence().Value;
        if (session.GetMaxRunningCadence().HasValue)
            sessionData["maxRunningCadence"] = session.GetMaxRunningCadence().Value;
        if (session.GetAvgCadence().HasValue)
            sessionData["avgCadence"] = session.GetAvgCadence().Value;
        if (session.GetMaxPower().HasValue)
            sessionData["maxPower"] = session.GetMaxPower().Value;
        if (session.GetAvgPower().HasValue)
            sessionData["avgPower"] = session.GetAvgPower().Value;
        if (session.GetTotalAscent().HasValue)
            sessionData["totalAscent"] = session.GetTotalAscent().Value;
        if (session.GetTotalDescent().HasValue)
            sessionData["totalDescent"] = session.GetTotalDescent().Value;
        if (session.GetMaxAltitude().HasValue)
            sessionData["maxAltitude"] = session.GetMaxAltitude().Value;
        if (session.GetMinAltitude().HasValue)
            sessionData["minAltitude"] = session.GetMinAltitude().Value;
        if (session.GetMaxPosGrade().HasValue)
            sessionData["maxPosGrade"] = session.GetMaxPosGrade().Value;
        if (session.GetMaxNegGrade().HasValue)
            sessionData["maxNegGrade"] = session.GetMaxNegGrade().Value;
        if (session.GetMaxTemperature().HasValue)
            sessionData["maxTemperature"] = session.GetMaxTemperature().Value;
        if (session.GetMinTemperature().HasValue)
            sessionData["minTemperature"] = session.GetMinTemperature().Value;
        if (session.GetTotalTrainingEffect().HasValue)
            sessionData["totalTrainingEffect"] = session.GetTotalTrainingEffect().Value;
        if (session.GetTotalAnaerobicTrainingEffect().HasValue)
            sessionData["totalAnaerobicTrainingEffect"] = session.GetTotalAnaerobicTrainingEffect().Value;
        if (session.GetMaxPosVerticalSpeed().HasValue)
            sessionData["maxPosVerticalSpeed"] = session.GetMaxPosVerticalSpeed().Value;
        if (session.GetMaxNegVerticalSpeed().HasValue)
            sessionData["maxNegVerticalSpeed"] = session.GetMaxNegVerticalSpeed().Value;
        if (session.GetTotalWork().HasValue)
            sessionData["totalWork"] = session.GetTotalWork().Value;
        if (session.GetTotalGrit().HasValue)
            sessionData["totalGrit"] = session.GetTotalGrit().Value;
        if (session.GetAvgFlow().HasValue)
            sessionData["avgFlow"] = session.GetAvgFlow().Value;

        // Extract device info
        var deviceData = new Dictionary<string, object?>();
        var deviceInfo = deviceInfos.FirstOrDefault();
        if (deviceInfo != null)
        {
            if (deviceInfo.GetManufacturer().HasValue)
                deviceData["manufacturer"] = deviceInfo.GetManufacturer().Value;
            if (deviceInfo.GetProduct().HasValue)
                deviceData["product"] = deviceInfo.GetProduct().Value;
            if (deviceInfo.GetSerialNumber().HasValue)
                deviceData["serialNumber"] = deviceInfo.GetSerialNumber().Value;
        }

        var rawFitData = new
        {
            session = sessionData.Count > 0 ? sessionData : null,
            device = deviceData.Count > 0 ? deviceData : null,
            recordCount = recordCount,
            hasTimeSeries = recordCount > 0,
            source = "fit_import",
            importedAt = System.DateTime.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(rawFitData, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
}

