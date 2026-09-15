using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Api.Models;
using Tempo.Api.Utils;
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
        public List<TrackPoint> TrackPoints { get; set; } = new();
        public List<TrackPoint> SeriesPoints { get; set; } = new();
        public string? RawFitDataJson { get; set; }
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
            var trackPoints = new List<TrackPoint>();
            var seriesPoints = new List<TrackPoint>();
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
                    var point = new TrackPoint
                    {
                        Latitude = latitude.Value,
                        Longitude = longitude.Value,
                        Time = timestamp.Value,
                        Elevation = altitude,
                        HeartRateBpm = record.GetHeartRate(),
                        CadenceRpm = Cadence.StepsPerMinuteFromFit(record.GetCadence()),
                        PowerWatts = record.GetPower(),
                        TemperatureC = record.GetTemperature()
                    };

                    trackPoints.Add(point);
                }

                seriesPoints.Add(MapRecordToSeriesPoint(record, timestamp.Value, latitude, longitude));

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

            var rawFitData = BuildRawFitData(session, deviceInfos, records.Count, weatherConditions, trackPoints);

            return new FitParseResult
            {
                StartTime = System.DateTime.SpecifyKind(startTime.Value, System.DateTimeKind.Utc),
                DurationSeconds = durationSeconds,
                DistanceMeters = totalDistance,
                TrackPoints = trackPoints,
                SeriesPoints = seriesPoints,
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

    private static TrackPoint MapRecordToSeriesPoint(
        RecordMesg record,
        System.DateTime timestamp,
        double? latitude,
        double? longitude)
    {
        var enhancedSpeed = record.GetEnhancedSpeed();
        var standardSpeed = record.GetSpeed();
        double? validatedSpeed = null;
        if (enhancedSpeed.HasValue && !double.IsNaN(enhancedSpeed.Value) && !double.IsInfinity(enhancedSpeed.Value) && enhancedSpeed.Value >= 0)
        {
            validatedSpeed = enhancedSpeed.Value;
        }
        else if (standardSpeed.HasValue && !double.IsNaN(standardSpeed.Value) && !double.IsInfinity(standardSpeed.Value) && standardSpeed.Value >= 0)
        {
            validatedSpeed = standardSpeed.Value;
        }

        var grade = record.GetGrade();
        double? validatedGrade = null;
        if (grade.HasValue)
        {
            var gradeValue = (double)grade.Value;
            if (!double.IsNaN(gradeValue) && !double.IsInfinity(gradeValue))
            {
                validatedGrade = Math.Max(-100.0, Math.Min(100.0, gradeValue));
            }
        }

        var verticalSpeed = record.GetVerticalSpeed();
        double? validatedVerticalSpeed = null;
        if (verticalSpeed.HasValue)
        {
            var vsValue = verticalSpeed.Value;
            if (!double.IsNaN(vsValue) && !double.IsInfinity(vsValue) && vsValue >= -50.0 && vsValue <= 50.0)
            {
                validatedVerticalSpeed = vsValue;
            }
        }

        double? elevation = null;
        var enhancedAltitude = record.GetEnhancedAltitude();
        var standardAltitude = record.GetAltitude();
        if (enhancedAltitude.HasValue && !double.IsNaN(enhancedAltitude.Value) && !double.IsInfinity(enhancedAltitude.Value))
        {
            elevation = enhancedAltitude.Value;
        }
        else if (standardAltitude.HasValue && !double.IsNaN(standardAltitude.Value) && !double.IsInfinity(standardAltitude.Value))
        {
            elevation = standardAltitude.Value;
        }

        double? distance = null;
        var distanceValue = record.GetDistance();
        if (distanceValue.HasValue)
        {
            var dist = distanceValue.Value;
            if (!double.IsNaN(dist) && !double.IsInfinity(dist) && dist >= 0)
            {
                distance = dist;
            }
        }

        return new TrackPoint
        {
            Latitude = latitude,
            Longitude = longitude,
            Time = timestamp,
            Elevation = elevation,
            HeartRateBpm = record.GetHeartRate(),
            CadenceRpm = Cadence.StepsPerMinuteFromFit(record.GetCadence()),
            PowerWatts = record.GetPower(),
            TemperatureC = record.GetTemperature(),
            SpeedMps = validatedSpeed,
            GradePercent = validatedGrade,
            VerticalSpeedMps = validatedVerticalSpeed,
            DistanceM = distance
        };
    }

    private string? BuildRawFitData(SessionMesg? session, ReadOnlyCollection<DeviceInfoMesg> deviceInfos, int recordCount, ReadOnlyCollection<WeatherConditionsMesg> weatherConditions, List<TrackPoint> trackPoints)
    {
        // Extract session data if available (nullable to handle FIT files without session messages)
        var sessionData = session != null ? ExtractSessionData(session) : null;
        var deviceData = ExtractDeviceData(deviceInfos);
        var weatherData = ExtractWeatherData(weatherConditions);

        var rawFitData = new
        {
            cadenceUnit = "spm",
            session = sessionData?.Count > 0 ? sessionData : null,
            device = deviceData.Count > 0 ? deviceData : null,
            weather = weatherData?.Count > 0 ? weatherData : null,
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
            recordCount = recordCount,
            hasTimeSeries = recordCount > 0,
            source = "fit_import",
            importedAt = System.DateTime.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(rawFitData, JsonUtils.DefaultOptions);
    }

    private static void SetIfPresent<T>(Dictionary<string, object?> dest, string key, T? value)
        where T : struct
    {
        if (value.HasValue)
            dest[key] = value.Value;
    }

    private static void SetIfPresent<T>(Dictionary<string, object?> dest, string key, T? value, Func<T, object> map)
        where T : struct
    {
        if (value.HasValue)
            dest[key] = map(value.Value);
    }

    private Dictionary<string, object?> ExtractSessionData(SessionMesg session)
    {
        var sessionData = new Dictionary<string, object?>();

        SetIfPresent(sessionData, "totalElapsedTime", session.GetTotalElapsedTime());
        SetIfPresent(sessionData, "totalTimerTime", session.GetTotalTimerTime());
        SetIfPresent(sessionData, "totalMovingTime", session.GetTotalMovingTime());
        SetIfPresent(sessionData, "totalDistance", session.GetTotalDistance());
        SetIfPresent(sessionData, "totalCycles", session.GetTotalCycles());
        SetIfPresent(sessionData, "totalStrides", session.GetTotalStrides());
        SetIfPresent(sessionData, "totalStrokes", session.GetTotalStrokes());
        SetIfPresent(sessionData, "totalCalories", session.GetTotalCalories());
        SetIfPresent(sessionData, "totalFatCalories", session.GetTotalFatCalories());
        SetIfPresent(sessionData, "maxSpeed", session.GetMaxSpeed());
        SetIfPresent(sessionData, "avgSpeed", session.GetAvgSpeed());
        SetIfPresent(sessionData, "maxHeartRate", session.GetMaxHeartRate());
        SetIfPresent(sessionData, "avgHeartRate", session.GetAvgHeartRate());
        SetIfPresent(sessionData, "minHeartRate", session.GetMinHeartRate());
        SetIfPresent(sessionData, "maxCadence", Cadence.StepsPerMinuteFromFit(session.GetMaxCadence()));
        SetIfPresent(sessionData, "maxRunningCadence", session.GetMaxRunningCadence());
        SetIfPresent(sessionData, "avgCadence", Cadence.StepsPerMinuteFromFit(session.GetAvgCadence()));
        SetIfPresent(sessionData, "maxPower", session.GetMaxPower());
        SetIfPresent(sessionData, "avgPower", session.GetAvgPower());
        SetIfPresent(sessionData, "totalAscent", session.GetTotalAscent());
        SetIfPresent(sessionData, "totalDescent", session.GetTotalDescent());
        SetIfPresent(sessionData, "maxAltitude", session.GetMaxAltitude());
        SetIfPresent(sessionData, "minAltitude", session.GetMinAltitude());
        SetIfPresent(sessionData, "maxPosGrade", session.GetMaxPosGrade());
        SetIfPresent(sessionData, "maxNegGrade", session.GetMaxNegGrade());
        SetIfPresent(sessionData, "maxTemperature", session.GetMaxTemperature());
        SetIfPresent(sessionData, "minTemperature", session.GetMinTemperature());
        SetIfPresent(sessionData, "totalTrainingEffect", session.GetTotalTrainingEffect());
        SetIfPresent(sessionData, "totalAnaerobicTrainingEffect", session.GetTotalAnaerobicTrainingEffect());
        SetIfPresent(sessionData, "maxPosVerticalSpeed", session.GetMaxPosVerticalSpeed());
        SetIfPresent(sessionData, "maxNegVerticalSpeed", session.GetMaxNegVerticalSpeed());
        SetIfPresent(sessionData, "totalWork", session.GetTotalWork());
        SetIfPresent(sessionData, "totalGrit", session.GetTotalGrit());
        SetIfPresent(sessionData, "avgFlow", session.GetAvgFlow());

        return sessionData;
    }

    private Dictionary<string, object?> ExtractDeviceData(ReadOnlyCollection<DeviceInfoMesg> deviceInfos)
    {
        var deviceData = new Dictionary<string, object?>();

        // Prefer device with SourceType = Local (5) as that's the recording device
        // According to FIT spec: Local indicates the device that recorded the activity
        DeviceInfoMesg? deviceInfo = deviceInfos.FirstOrDefault(d => d.GetSourceType() == SourceType.Local)
            ?? deviceInfos.FirstOrDefault();

        if (deviceInfo != null)
        {
            SetIfPresent(deviceData, "manufacturer", deviceInfo.GetManufacturer());
            SetIfPresent(deviceData, "product", deviceInfo.GetProduct());
            SetIfPresent(deviceData, "serialNumber", deviceInfo.GetSerialNumber());

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

        return deviceData;
    }

    private Dictionary<string, object?>? ExtractWeatherData(ReadOnlyCollection<WeatherConditionsMesg> weatherConditions)
    {
        var weatherCondition = weatherConditions.FirstOrDefault();
        if (weatherCondition == null)
        {
            return null;
        }

        var weatherData = new Dictionary<string, object?>();

        SetIfPresent(weatherData, "weatherReport", weatherCondition.GetWeatherReport(), v => v.ToString());
        SetIfPresent(weatherData, "temperature", weatherCondition.GetTemperature());
        SetIfPresent(weatherData, "condition", weatherCondition.GetCondition(), v => v.ToString());
        SetIfPresent(weatherData, "windDirection", weatherCondition.GetWindDirection());
        SetIfPresent(weatherData, "windSpeed", weatherCondition.GetWindSpeed());
        SetIfPresent(weatherData, "precipitationProbability", weatherCondition.GetPrecipitationProbability());
        SetIfPresent(weatherData, "temperatureFeelsLike", weatherCondition.GetTemperatureFeelsLike());
        SetIfPresent(weatherData, "relativeHumidity", weatherCondition.GetRelativeHumidity());
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
        SetIfPresent(weatherData, "observedLocationLat", weatherCondition.GetObservedLocationLat());
        SetIfPresent(weatherData, "observedLocationLong", weatherCondition.GetObservedLocationLong());
        SetIfPresent(weatherData, "dayOfWeek", weatherCondition.GetDayOfWeek(), v => v.ToString());
        SetIfPresent(weatherData, "highTemperature", weatherCondition.GetHighTemperature());
        SetIfPresent(weatherData, "lowTemperature", weatherCondition.GetLowTemperature());

        return weatherData;
    }
}

