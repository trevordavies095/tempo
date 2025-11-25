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
    private readonly ElevationCalculationConfig _elevationConfig;

    public FitParserService(ElevationCalculationConfig elevationConfig)
    {
        _elevationConfig = elevationConfig;
    }

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
            var weatherConditions = messages.WeatherConditionsMesgs;

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

            // Calculate elevation gain with noise filtering
            double? elevationGain = CalculateElevationGain(trackPoints);

            // Build RawFitData JSON
            var rawFitData = BuildRawFitData(session, deviceInfos, records.Count, weatherConditions);

            return new FitParseResult
            {
                StartTime = System.DateTime.SpecifyKind(startTime.Value, System.DateTimeKind.Utc),
                DurationSeconds = durationSeconds,
                DistanceMeters = totalDistance,
                ElevationGainMeters = elevationGain,
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

    private double? CalculateElevationGain(List<GpxParserService.GpxPoint> trackPoints)
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
        GpxParserService.GpxPoint? lastPoint = null;

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

    private string? BuildRawFitData(SessionMesg? session, ReadOnlyCollection<DeviceInfoMesg> deviceInfos, int recordCount, ReadOnlyCollection<WeatherConditionsMesg> weatherConditions)
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
        // Prefer device with SourceType = Local (5) as that's the recording device
        // According to FIT spec: Local indicates the device that recorded the activity
        var deviceData = new Dictionary<string, object?>();
        DeviceInfoMesg? deviceInfo = null;
        
        // First, try to find device with SourceType = Local (recording device)
        var localDevice = deviceInfos.FirstOrDefault(d => d.GetSourceType() == SourceType.Local);
        if (localDevice != null)
        {
            deviceInfo = localDevice;
        }
        else
        {
            // Fallback to first device if no Local device found
            deviceInfo = deviceInfos.FirstOrDefault();
        }
        
        if (deviceInfo != null)
        {
            if (deviceInfo.GetManufacturer().HasValue)
                deviceData["manufacturer"] = deviceInfo.GetManufacturer().Value;
            if (deviceInfo.GetProduct().HasValue)
                deviceData["product"] = deviceInfo.GetProduct().Value;
            if (deviceInfo.GetSerialNumber().HasValue)
                deviceData["serialNumber"] = deviceInfo.GetSerialNumber().Value;
            
            // Extract ProductName field if available (most reliable device name)
            try
            {
                var productName = deviceInfo.GetProductNameAsString();
                if (!string.IsNullOrWhiteSpace(productName))
                {
                    deviceData["productName"] = productName;
                }
            }
            catch
            {
                // ProductName may not be available - ignore
            }
        }

        // Extract weather data from WeatherConditionsMesg
        Dictionary<string, object?>? weatherData = null;
        var weatherCondition = weatherConditions.FirstOrDefault();
        if (weatherCondition != null)
        {
            weatherData = new Dictionary<string, object?>();
            
            if (weatherCondition.GetWeatherReport().HasValue)
                weatherData["weatherReport"] = weatherCondition.GetWeatherReport().Value.ToString();
            if (weatherCondition.GetTemperature().HasValue)
                weatherData["temperature"] = weatherCondition.GetTemperature().Value;
            if (weatherCondition.GetCondition().HasValue)
                weatherData["condition"] = weatherCondition.GetCondition().Value.ToString();
            if (weatherCondition.GetWindDirection().HasValue)
                weatherData["windDirection"] = weatherCondition.GetWindDirection().Value;
            if (weatherCondition.GetWindSpeed().HasValue)
                weatherData["windSpeed"] = weatherCondition.GetWindSpeed().Value;
            if (weatherCondition.GetPrecipitationProbability().HasValue)
                weatherData["precipitationProbability"] = weatherCondition.GetPrecipitationProbability().Value;
            if (weatherCondition.GetTemperatureFeelsLike().HasValue)
                weatherData["temperatureFeelsLike"] = weatherCondition.GetTemperatureFeelsLike().Value;
            if (weatherCondition.GetRelativeHumidity().HasValue)
                weatherData["relativeHumidity"] = weatherCondition.GetRelativeHumidity().Value;
            var locationStr = weatherCondition.GetLocationAsString();
            if (!string.IsNullOrEmpty(locationStr))
                weatherData["location"] = locationStr;
            try
            {
                var observedAtTime = weatherCondition.GetObservedAtTime();
                // GetObservedAtTime() returns Dynastream.Fit.DateTime, convert to System.DateTime
                if (observedAtTime != null)
                    weatherData["observedAtTime"] = observedAtTime.GetDateTime().ToString("O");
            }
            catch
            {
                // ObservedAtTime may be null/invalid - skip it
            }
            if (weatherCondition.GetObservedLocationLat().HasValue)
                weatherData["observedLocationLat"] = weatherCondition.GetObservedLocationLat().Value;
            if (weatherCondition.GetObservedLocationLong().HasValue)
                weatherData["observedLocationLong"] = weatherCondition.GetObservedLocationLong().Value;
            if (weatherCondition.GetDayOfWeek().HasValue)
                weatherData["dayOfWeek"] = weatherCondition.GetDayOfWeek().Value.ToString();
            if (weatherCondition.GetHighTemperature().HasValue)
                weatherData["highTemperature"] = weatherCondition.GetHighTemperature().Value;
            if (weatherCondition.GetLowTemperature().HasValue)
                weatherData["lowTemperature"] = weatherCondition.GetLowTemperature().Value;
        }

        var rawFitData = new
        {
            session = sessionData.Count > 0 ? sessionData : null,
            device = deviceData.Count > 0 ? deviceData : null,
            weather = weatherData?.Count > 0 ? weatherData : null,
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

