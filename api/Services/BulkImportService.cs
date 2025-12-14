using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Service for bulk importing workouts from Strava export ZIP files.
/// </summary>
public class BulkImportService
{
    private readonly TempoDbContext _db;
    private readonly GpxParserService _gpxParser;
    private readonly StravaCsvParserService _csvParser;
    private readonly FitParserService _fitParser;
    private readonly MediaService _mediaService;
    private readonly WeatherService _weatherService;
    private readonly HeartRateZoneService _zoneService;
    private readonly RelativeEffortService _relativeEffortService;
    private readonly ILogger<BulkImportService> _logger;

    public BulkImportService(
        TempoDbContext db,
        GpxParserService gpxParser,
        StravaCsvParserService csvParser,
        FitParserService fitParser,
        MediaService mediaService,
        WeatherService weatherService,
        HeartRateZoneService zoneService,
        RelativeEffortService relativeEffortService,
        ILogger<BulkImportService> logger)
    {
        _db = db;
        _gpxParser = gpxParser;
        _csvParser = csvParser;
        _fitParser = fitParser;
        _mediaService = mediaService;
        _weatherService = weatherService;
        _zoneService = zoneService;
        _relativeEffortService = relativeEffortService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the CSV parser service (for GetRunActivities method).
    /// </summary>
    public StravaCsvParserService GetCsvParser()
    {
        return _csvParser;
    }

    /// <summary>
    /// Extracts a ZIP archive to a temporary directory.
    /// </summary>
    public string ExtractZipArchive(Stream zipStream)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                var entryPath = Path.Combine(tempDir, entry.FullName);
                var entryDir = Path.GetDirectoryName(entryPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                if (!string.IsNullOrEmpty(entry.Name))
                {
                    using (var entryStream = entry.Open())
                    using (var fileStream = new FileStream(entryPath, FileMode.Create))
                    {
                        entryStream.CopyTo(fileStream);
                    }
                }
            }
        }

        return tempDir;
    }

    /// <summary>
    /// Parses the activities.csv file from the extracted directory.
    /// </summary>
    public List<StravaCsvParserService.StravaActivityRecord> ParseActivitiesCsv(string tempDir)
    {
        var csvPath = Path.Combine(tempDir, "activities.csv");
        if (!File.Exists(csvPath))
        {
            throw new InvalidOperationException("ZIP file must contain activities.csv in the root");
        }

        using (var csvStream = File.OpenRead(csvPath))
        {
            return _csvParser.ParseActivitiesCsv(csvStream);
        }
    }

    /// <summary>
    /// Processes a single activity file and creates/updates a workout.
    /// </summary>
    public async Task<ActivityProcessResult> ProcessActivityFileAsync(
        StravaCsvParserService.StravaActivityRecord activity,
        string tempDir,
        double splitDistanceMeters)
    {
        var filePath = Path.Combine(tempDir, activity.Filename.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(filePath))
        {
            return new ActivityProcessResult
            {
                Success = false,
                ErrorMessage = "File not found in ZIP archive"
            };
        }

        // Determine file type
        string? fileType = null;
        bool isFitGz = false;
        if (filePath.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase))
        {
            fileType = "gpx";
        }
        else if (filePath.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase))
        {
            fileType = "fit";
            isFitGz = true;
        }
        else if (filePath.EndsWith(".fit", StringComparison.OrdinalIgnoreCase))
        {
            fileType = "fit";
        }
        else
        {
            return new ActivityProcessResult
            {
                Success = false,
                ErrorMessage = "Unsupported file format. Only .gpx and .fit/.fit.gz files are supported."
            };
        }

        try
        {
            // Read file into byte array before parsing
            byte[] rawFileData;
            using (var fileStream = File.OpenRead(filePath))
            using (var memoryStream = new MemoryStream())
            {
                await fileStream.CopyToAsync(memoryStream);
                rawFileData = memoryStream.ToArray();
            }

            // Parse the activity file
            GpxParserService.GpxParseResult? parseResult = null;
            FitParserService.FitParseResult? fitResult = null;

            if (fileType == "gpx")
            {
                using (var stream = new MemoryStream(rawFileData))
                {
                    parseResult = _gpxParser.ParseGpx(stream);
                }
            }
            else if (fileType == "fit")
            {
                try
                {
                    using (var stream = new MemoryStream(rawFileData))
                    {
                        if (isFitGz)
                        {
                            fitResult = _fitParser.ParseGzippedFit(stream);
                        }
                        else
                        {
                            fitResult = _fitParser.ParseFit(stream);
                        }
                    }
                }
                catch (NotSupportedException ex)
                {
                    return new ActivityProcessResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message
                    };
                }
            }

            // Extract data from parse result
            DateTime startTime;
            int durationSeconds;
            double distanceMeters;
            double? elevationGainMeters;
            List<GpxParserService.GpxPoint> trackPoints;
            string? rawGpxDataJson = null;
            string? rawFitDataJson = null;

            if (parseResult != null)
            {
                startTime = parseResult.StartTime;
                durationSeconds = parseResult.DurationSeconds;
                distanceMeters = parseResult.DistanceMeters;
                elevationGainMeters = parseResult.ElevationGainMeters;
                trackPoints = parseResult.TrackPoints;
                rawGpxDataJson = parseResult.RawGpxDataJson;
            }
            else if (fitResult != null)
            {
                startTime = fitResult.StartTime;
                durationSeconds = fitResult.DurationSeconds;
                distanceMeters = fitResult.DistanceMeters;
                elevationGainMeters = fitResult.ElevationGainMeters;
                trackPoints = fitResult.TrackPoints;
                rawFitDataJson = fitResult.RawFitDataJson;
            }
            else
            {
                return new ActivityProcessResult
                {
                    Success = false,
                    ErrorMessage = "Failed to parse file"
                };
            }

            // Ensure StartedAt is UTC
            var startedAtUtc = startTime.Kind switch
            {
                DateTimeKind.Utc => startTime,
                DateTimeKind.Local => startTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
            };

            // Check for duplicate
            var existingWorkout = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startedAtUtc, distanceMeters, durationSeconds);

            if (existingWorkout != null)
            {
                // Check if existing workout is missing raw file data
                bool needsRawFileUpdate = existingWorkout.RawFileData == null || existingWorkout.RawFileData.Length == 0;
                
                if (needsRawFileUpdate)
                {
                    // Backfill raw file data for existing workout
                    existingWorkout.RawFileData = rawFileData;
                    existingWorkout.RawFileName = Path.GetFileName(activity.Filename);
                    existingWorkout.RawFileType = fileType;
                    
                    // Save the update immediately
                    await _db.SaveChangesAsync();
                    
                    _logger.LogInformation("Updated duplicate workout {WorkoutId} with raw file data: {Filename} at {StartTime}", 
                        existingWorkout.Id, activity.Filename, startTime);
                    
                    return new ActivityProcessResult
                    {
                        Success = true,
                        Action = "updated",
                        Workout = existingWorkout,
                        MediaPaths = !string.IsNullOrWhiteSpace(activity.Media) 
                            ? activity.Media.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                            : new List<string>()
                    };
                }
                else
                {
                    _logger.LogInformation("Skipped duplicate workout (already has raw file): {Filename} at {StartTime}", 
                        activity.Filename, startTime);
                    
                    return new ActivityProcessResult
                    {
                        Success = true,
                        Action = "skipped",
                        Workout = existingWorkout,
                        MediaPaths = !string.IsNullOrWhiteSpace(activity.Media) 
                            ? activity.Media.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                            : new List<string>()
                    };
                }
            }

            // Create new workout
            var workout = CreateWorkoutFromActivity(
                activity, startedAtUtc, durationSeconds, distanceMeters, elevationGainMeters,
                rawFileData, fileType, rawGpxDataJson, rawFitDataJson, parseResult, fitResult, trackPoints);

            // Create route
            var route = CreateWorkoutRoute(workout.Id, trackPoints);

            // Calculate splits
            var splits = CalculateSplits(trackPoints, distanceMeters, durationSeconds, splitDistanceMeters, workout.Id);

            // Create time-series from GPX track points if available
            List<WorkoutTimeSeries> timeSeries = new List<WorkoutTimeSeries>();
            if (parseResult != null)
            {
                timeSeries = CreateTimeSeriesFromGpxTrackPoints(workout.Id, startedAtUtc, trackPoints);
                if (timeSeries.Count > 0)
                {
                    CalculateAggregateMetricsFromTimeSeries(workout, timeSeries);
                }
            }
            // Create time-series from FIT records if available
            else if (fitResult != null && fitResult.RecordMesgs.Count > 0)
            {
                timeSeries = CreateTimeSeriesFromFitRecords(workout.Id, startedAtUtc, fitResult.RecordMesgs);
                if (timeSeries.Count > 0)
                {
                    CalculateAggregateMetricsFromTimeSeries(workout, timeSeries);
                }
            }

            // Fetch weather data
            await FetchAndAttachWeatherAsync(workout, trackPoints, activity.RawStravaDataJson, fitResult?.RawFitDataJson, startedAtUtc);

            return new ActivityProcessResult
            {
                Success = true,
                Action = "created",
                Workout = workout,
                Route = route,
                Splits = splits,
                TimeSeries = timeSeries.Count > 0 ? timeSeries : null,
                MediaPaths = !string.IsNullOrWhiteSpace(activity.Media) 
                    ? activity.Media.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                    : new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing activity file {Filename}", activity.Filename);
            return new ActivityProcessResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Creates a workout entity from activity data.
    /// </summary>
    private Workout CreateWorkoutFromActivity(
        StravaCsvParserService.StravaActivityRecord activity,
        DateTime startedAtUtc,
        int durationSeconds,
        double distanceMeters,
        double? elevationGainMeters,
        byte[] rawFileData,
        string fileType,
        string? rawGpxDataJson,
        string? rawFitDataJson,
        GpxParserService.GpxParseResult? parseResult,
        FitParserService.FitParseResult? fitResult,
        List<GpxParserService.GpxPoint> trackPoints)
    {
        // Calculate average pace
        var avgPaceS = distanceMeters > 0 && durationSeconds > 0
            ? (int)(durationSeconds / (distanceMeters / 1000.0))
            : 0;

        // Build notes from CSV metadata
        var notesParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(activity.ActivityDescription))
        {
            notesParts.Add(activity.ActivityDescription);
        }
        if (!string.IsNullOrWhiteSpace(activity.ActivityPrivateNote))
        {
            notesParts.Add(activity.ActivityPrivateNote);
        }
        var notes = notesParts.Count > 0 ? string.Join("\n\n", notesParts) : null;

        // Extract metrics from RawStravaData JSON
        var stravaData = ExtractStravaData(activity.RawStravaDataJson);

        // Extract metrics from GPX/FIT calculated data
        var calculated = ExtractCalculatedMetrics(parseResult?.RawGpxDataJson);

        // Create workout
        var workout = new Workout
        {
            Id = Guid.NewGuid(),
            StartedAt = startedAtUtc,
            DurationS = durationSeconds,
            DistanceM = distanceMeters,
            AvgPaceS = avgPaceS,
            ElevGainM = elevationGainMeters,
            RawGpxData = parseResult?.RawGpxDataJson,
            RawFitData = fitResult?.RawFitDataJson,
            RawStravaData = activity.RawStravaDataJson,
            Source = "strava_import",
            Name = !string.IsNullOrWhiteSpace(activity.ActivityName) ? activity.ActivityName : null,
            Notes = notes,
            RawFileData = rawFileData,
            RawFileName = Path.GetFileName(activity.Filename),
            RawFileType = fileType,
            RunType = "Easy Run",
            CreatedAt = DateTime.UtcNow
        };

        // Populate additional metrics from calculated data (GPX)
        PopulateMetricsFromCalculated(workout, calculated);

        // Populate metrics from FIT session data
        PopulateMetricsFromFit(workout, fitResult?.RawFitDataJson);

        // Populate metrics from Strava CSV data
        PopulateMetricsFromStrava(workout, stravaData);

        // Infer device from Source field if device is missing or "Development"
        if (string.IsNullOrWhiteSpace(workout.Device) || workout.Device == "Development")
        {
            if (workout.Source == "apple_watch")
            {
                workout.Device = "Apple Watch";
            }
        }

        return workout;
    }

    /// <summary>
    /// Extracts Strava data from JSON.
    /// </summary>
    private Dictionary<string, object> ExtractStravaData(string? rawStravaDataJson)
    {
        var stravaData = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(rawStravaDataJson))
        {
            try
            {
                var rawStrava = JsonSerializer.Deserialize<JsonElement>(rawStravaDataJson);
                foreach (var prop in rawStrava.EnumerateObject())
                {
                    stravaData[prop.Name] = prop.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse RawStravaData JSON");
            }
        }
        return stravaData;
    }

    /// <summary>
    /// Extracts calculated metrics from GPX data.
    /// </summary>
    private Dictionary<string, object> ExtractCalculatedMetrics(string? rawGpxDataJson)
    {
        var calculated = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(rawGpxDataJson))
        {
            try
            {
                var rawGpx = JsonSerializer.Deserialize<JsonElement>(rawGpxDataJson);
                if (rawGpx.TryGetProperty("calculated", out var calculatedElement))
                {
                    foreach (var prop in calculatedElement.EnumerateObject())
                    {
                        calculated[prop.Name] = prop.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse RawGpxData JSON for additional metrics");
            }
        }
        return calculated;
    }

    /// <summary>
    /// Populates workout metrics from calculated GPX data.
    /// </summary>
    private void PopulateMetricsFromCalculated(Workout workout, Dictionary<string, object> calculated)
    {
        if (calculated.TryGetValue("elevLossM", out var elevLoss) && elevLoss is JsonElement elevLossElem && elevLossElem.ValueKind == JsonValueKind.Number)
            workout.ElevLossM = elevLossElem.GetDouble();
        if (calculated.TryGetValue("minElevM", out var minElev) && minElev is JsonElement minElevElem && minElevElem.ValueKind == JsonValueKind.Number)
            workout.MinElevM = minElevElem.GetDouble();
        if (calculated.TryGetValue("maxElevM", out var maxElev) && maxElev is JsonElement maxElevElem && maxElevElem.ValueKind == JsonValueKind.Number)
            workout.MaxElevM = maxElevElem.GetDouble();
        if (calculated.TryGetValue("maxSpeedMps", out var maxSpeed) && maxSpeed is JsonElement maxSpeedElem && maxSpeedElem.ValueKind == JsonValueKind.Number)
            workout.MaxSpeedMps = maxSpeedElem.GetDouble();
        if (calculated.TryGetValue("avgSpeedMps", out var avgSpeed) && avgSpeed is JsonElement avgSpeedElem && avgSpeedElem.ValueKind == JsonValueKind.Number)
            workout.AvgSpeedMps = avgSpeedElem.GetDouble();
    }

    /// <summary>
    /// Populates workout metrics from FIT session data.
    /// </summary>
    private void PopulateMetricsFromFit(Workout workout, string? rawFitDataJson)
    {
        if (string.IsNullOrEmpty(rawFitDataJson))
        {
            return;
        }

        try
        {
            var rawFit = JsonSerializer.Deserialize<JsonElement>(rawFitDataJson);
            if (rawFit.TryGetProperty("session", out var sessionElement))
            {
                if (sessionElement.TryGetProperty("totalMovingTime", out var movingTime) && movingTime.ValueKind == JsonValueKind.Number)
                    workout.MovingTimeS = (int)Math.Round(movingTime.GetDouble());
                if (sessionElement.TryGetProperty("maxHeartRate", out var maxHr) && maxHr.ValueKind == JsonValueKind.Number)
                    workout.MaxHeartRateBpm = (byte)maxHr.GetInt32();
                if (sessionElement.TryGetProperty("avgHeartRate", out var avgHr) && avgHr.ValueKind == JsonValueKind.Number)
                    workout.AvgHeartRateBpm = (byte)avgHr.GetInt32();
                if (sessionElement.TryGetProperty("minHeartRate", out var minHr) && minHr.ValueKind == JsonValueKind.Number)
                    workout.MinHeartRateBpm = (byte)minHr.GetInt32();
                if (sessionElement.TryGetProperty("maxCadence", out var maxCad) && maxCad.ValueKind == JsonValueKind.Number)
                    workout.MaxCadenceRpm = (byte)maxCad.GetInt32();
                if (sessionElement.TryGetProperty("avgCadence", out var avgCad) && avgCad.ValueKind == JsonValueKind.Number)
                    workout.AvgCadenceRpm = (byte)avgCad.GetInt32();
                if (sessionElement.TryGetProperty("maxPower", out var maxPow) && maxPow.ValueKind == JsonValueKind.Number)
                    workout.MaxPowerWatts = (ushort)maxPow.GetInt32();
                if (sessionElement.TryGetProperty("avgPower", out var avgPow) && avgPow.ValueKind == JsonValueKind.Number)
                    workout.AvgPowerWatts = (ushort)avgPow.GetInt32();
                if (sessionElement.TryGetProperty("totalCalories", out var cals) && cals.ValueKind == JsonValueKind.Number)
                    workout.Calories = (ushort)cals.GetInt32();
            }

            // Extract device information
            if (rawFit.TryGetProperty("device", out var deviceElement))
            {
                if (deviceElement.ValueKind == JsonValueKind.Object)
                {
                    _logger.LogDebug("Found device element in FIT file: {DeviceData}", deviceElement.GetRawText());
                    workout.Device = DeviceExtractionService.ExtractDeviceName(deviceElement, _logger);
                    if (string.IsNullOrWhiteSpace(workout.Device))
                    {
                        _logger.LogDebug("Device extraction returned null. Device element: {DeviceData}", deviceElement.GetRawText());
                    }
                }
                else
                {
                    _logger.LogDebug("Device element exists but is not an object. Type: {Type}, Value: {Value}", deviceElement.ValueKind, deviceElement.GetRawText());
                }
            }
            else
            {
                _logger.LogDebug("No device element found in RawFitData");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract metrics from RawFitData JSON");
        }
    }

    /// <summary>
    /// Populates workout metrics from Strava CSV data.
    /// </summary>
    private void PopulateMetricsFromStrava(Workout workout, Dictionary<string, object> stravaData)
    {
        if (stravaData.TryGetValue("movingTime", out var stravaMovingTime) && stravaMovingTime is JsonElement stravaMovingTimeElem && stravaMovingTimeElem.ValueKind == JsonValueKind.Number)
            workout.MovingTimeS = (int)Math.Round(stravaMovingTimeElem.GetDouble());
        if (stravaData.TryGetValue("maxHeartRate", out var stravaMaxHr) && stravaMaxHr is JsonElement stravaMaxHrElem && stravaMaxHrElem.ValueKind == JsonValueKind.Number)
            workout.MaxHeartRateBpm = (byte)stravaMaxHrElem.GetInt32();
        if (stravaData.TryGetValue("avgHeartRate", out var stravaAvgHr) && stravaAvgHr is JsonElement stravaAvgHrElem && stravaAvgHrElem.ValueKind == JsonValueKind.Number)
            workout.AvgHeartRateBpm = (byte)stravaAvgHrElem.GetInt32();
        if (stravaData.TryGetValue("maxCadence", out var stravaMaxCad) && stravaMaxCad is JsonElement stravaMaxCadElem && stravaMaxCadElem.ValueKind == JsonValueKind.Number)
            workout.MaxCadenceRpm = (byte)stravaMaxCadElem.GetInt32();
        if (stravaData.TryGetValue("avgCadence", out var stravaAvgCad) && stravaAvgCad is JsonElement stravaAvgCadElem && stravaAvgCadElem.ValueKind == JsonValueKind.Number)
            workout.AvgCadenceRpm = (byte)stravaAvgCadElem.GetInt32();
        if (stravaData.TryGetValue("maxWatts", out var stravaMaxWatts) && stravaMaxWatts is JsonElement stravaMaxWattsElem && stravaMaxWattsElem.ValueKind == JsonValueKind.Number)
            workout.MaxPowerWatts = (ushort)stravaMaxWattsElem.GetInt32();
        if (stravaData.TryGetValue("avgWatts", out var stravaAvgWatts) && stravaAvgWatts is JsonElement stravaAvgWattsElem && stravaAvgWattsElem.ValueKind == JsonValueKind.Number)
            workout.AvgPowerWatts = (ushort)stravaAvgWattsElem.GetInt32();
        if (stravaData.TryGetValue("calories", out var stravaCals) && stravaCals is JsonElement stravaCalsElem && stravaCalsElem.ValueKind == JsonValueKind.Number)
            workout.Calories = (ushort)stravaCalsElem.GetInt32();
        if (stravaData.TryGetValue("elevationLoss", out var stravaElevLoss) && stravaElevLoss is JsonElement stravaElevLossElem && stravaElevLossElem.ValueKind == JsonValueKind.Number)
            workout.ElevLossM = stravaElevLossElem.GetDouble();
        if (stravaData.TryGetValue("elevationLow", out var stravaMinElev) && stravaMinElev is JsonElement stravaMinElevElem && stravaMinElevElem.ValueKind == JsonValueKind.Number)
            workout.MinElevM = stravaMinElevElem.GetDouble();
        if (stravaData.TryGetValue("elevationHigh", out var stravaMaxElev) && stravaMaxElev is JsonElement stravaMaxElevElem && stravaMaxElevElem.ValueKind == JsonValueKind.Number)
            workout.MaxElevM = stravaMaxElevElem.GetDouble();
        if (stravaData.TryGetValue("maxSpeed", out var stravaMaxSpeed) && stravaMaxSpeed is JsonElement stravaMaxSpeedElem && stravaMaxSpeedElem.ValueKind == JsonValueKind.Number)
            workout.MaxSpeedMps = stravaMaxSpeedElem.GetDouble();
        if (stravaData.TryGetValue("avgSpeed", out var stravaAvgSpeed) && stravaAvgSpeed is JsonElement stravaAvgSpeedElem && stravaAvgSpeedElem.ValueKind == JsonValueKind.Number)
            workout.AvgSpeedMps = stravaAvgSpeedElem.GetDouble();
    }

    /// <summary>
    /// Creates a workout route from track points.
    /// </summary>
    private WorkoutRoute CreateWorkoutRoute(Guid workoutId, List<GpxParserService.GpxPoint> trackPoints)
    {
        var coordinates = trackPoints.Select(p => new[] { p.Longitude, p.Latitude }).ToList();
        var routeGeoJson = JsonSerializer.Serialize(new
        {
            type = "LineString",
            coordinates = coordinates
        });

        return new WorkoutRoute
        {
            Id = Guid.NewGuid(),
            WorkoutId = workoutId,
            RouteGeoJson = routeGeoJson
        };
    }

    /// <summary>
    /// Calculates splits for a workout.
    /// </summary>
    private List<WorkoutSplit> CalculateSplits(
        List<GpxParserService.GpxPoint> trackPoints,
        double distanceMeters,
        int durationSeconds,
        double splitDistanceMeters,
        Guid workoutId)
    {
        var splits = _gpxParser.CalculateSplits(
            trackPoints,
            distanceMeters,
            durationSeconds,
            splitDistanceMeters
        );

        foreach (var split in splits)
        {
            split.WorkoutId = workoutId;
        }

        return splits;
    }

    /// <summary>
    /// Creates time-series records from GPX track points with sensor data.
    /// </summary>
    private List<WorkoutTimeSeries> CreateTimeSeriesFromGpxTrackPoints(
        Guid workoutId,
        DateTime startTime,
        List<GpxParserService.GpxPoint> trackPoints)
    {
        var timeSeries = new List<WorkoutTimeSeries>();

        foreach (var point in trackPoints)
        {
            if (!point.Time.HasValue) continue;

            var elapsedSeconds = (int)(point.Time.Value - startTime).TotalSeconds;

            // Only create record if there's sensor data
            if (point.HeartRateBpm.HasValue ||
                point.CadenceRpm.HasValue ||
                point.PowerWatts.HasValue ||
                point.TemperatureC.HasValue)
            {
                timeSeries.Add(new WorkoutTimeSeries
                {
                    Id = Guid.NewGuid(),
                    WorkoutId = workoutId,
                    ElapsedSeconds = elapsedSeconds,
                    HeartRateBpm = point.HeartRateBpm,
                    CadenceRpm = point.CadenceRpm,
                    PowerWatts = point.PowerWatts,
                    TemperatureC = point.TemperatureC,
                    ElevationM = point.Elevation
                });
            }
        }

        return timeSeries;
    }

    /// <summary>
    /// Creates time-series records from FIT RecordMesg messages with sensor data.
    /// </summary>
    private List<WorkoutTimeSeries> CreateTimeSeriesFromFitRecords(
        Guid workoutId,
        DateTime startTime,
        ReadOnlyCollection<Dynastream.Fit.RecordMesg> records)
    {
        var timeSeries = new List<WorkoutTimeSeries>();

        foreach (var record in records)
        {
            var timestamp = record.GetTimestamp()?.GetDateTime().ToUniversalTime();
            if (timestamp == null) continue;

            var elapsedSeconds = (int)(timestamp.Value - startTime).TotalSeconds;
            if (elapsedSeconds < 0) continue; // Skip records before start time

            // Extract and validate all fields first
            // Extract and validate speed (must be non-negative, finite, and not NaN)
            // Prefer enhanced speed if valid, otherwise fall back to standard speed
            var enhancedSpeed = record.GetEnhancedSpeed();
            var standardSpeed = record.GetSpeed();
            double? validatedSpeed = null;
            if (enhancedSpeed.HasValue && !double.IsNaN(enhancedSpeed.Value) && !double.IsInfinity(enhancedSpeed.Value) && enhancedSpeed.Value >= 0)
            {
                validatedSpeed = (double?)enhancedSpeed.Value;
            }
            else if (standardSpeed.HasValue && !double.IsNaN(standardSpeed.Value) && !double.IsInfinity(standardSpeed.Value) && standardSpeed.Value >= 0)
            {
                validatedSpeed = (double?)standardSpeed.Value;
            }

            // Extract and validate grade (clamp to -100 to 100 range, exclude NaN and Infinity)
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

            // Extract and validate vertical speed (reasonable range -50 to 50 m/s, exclude NaN and Infinity)
            var verticalSpeed = record.GetVerticalSpeed();
            double? validatedVerticalSpeed = null;
            if (verticalSpeed.HasValue)
            {
                var vsValue = verticalSpeed.Value;
                if (!double.IsNaN(vsValue) && !double.IsInfinity(vsValue) && vsValue >= -50.0 && vsValue <= 50.0)
                {
                    validatedVerticalSpeed = (double)vsValue;
                }
                // Otherwise, set to null (invalid data)
            }

            // Extract other fields (validate NaN for double fields)
            var heartRate = record.GetHeartRate();
            var cadence = record.GetCadence();
            var power = record.GetPower();
            var temperature = record.GetTemperature();
            
            // Extract elevation (prefer enhanced, exclude NaN and Infinity)
            double? elevation = null;
            var enhancedAltitude = record.GetEnhancedAltitude();
            var standardAltitude = record.GetAltitude();
            if (enhancedAltitude.HasValue && !double.IsNaN(enhancedAltitude.Value) && !double.IsInfinity(enhancedAltitude.Value))
            {
                elevation = (double?)enhancedAltitude.Value;
            }
            else if (standardAltitude.HasValue && !double.IsNaN(standardAltitude.Value) && !double.IsInfinity(standardAltitude.Value))
            {
                elevation = (double?)standardAltitude.Value;
            }
            
            // Extract distance (must be non-negative, finite, and not NaN)
            double? distance = null;
            var distanceValue = record.GetDistance();
            if (distanceValue.HasValue)
            {
                var dist = distanceValue.Value;
                if (!double.IsNaN(dist) && !double.IsInfinity(dist) && dist >= 0)
                {
                    distance = (double?)dist;
                }
            }

            // Only create record if there's at least one valid data field after validation
            var hasValidData = heartRate.HasValue ||
                               cadence.HasValue ||
                               power.HasValue ||
                               validatedSpeed.HasValue ||
                               temperature.HasValue ||
                               elevation.HasValue ||
                               validatedGrade.HasValue ||
                               validatedVerticalSpeed.HasValue ||
                               distance.HasValue;

            if (hasValidData)
            {
                var timeSeriesRecord = new WorkoutTimeSeries
                {
                    Id = Guid.NewGuid(),
                    WorkoutId = workoutId,
                    ElapsedSeconds = elapsedSeconds,
                    HeartRateBpm = heartRate,
                    CadenceRpm = cadence,
                    PowerWatts = power,
                    SpeedMps = validatedSpeed,
                    TemperatureC = temperature,
                    ElevationM = elevation,
                    GradePercent = validatedGrade,
                    VerticalSpeedMps = validatedVerticalSpeed,
                    DistanceM = distance
                };

                timeSeries.Add(timeSeriesRecord);
            }
        }

        return timeSeries;
    }

    /// <summary>
    /// Calculates aggregate metrics (max/avg/min) from time-series data and updates workout.
    /// </summary>
    private void CalculateAggregateMetricsFromTimeSeries(Workout workout, List<WorkoutTimeSeries> timeSeries)
    {
        if (timeSeries == null || timeSeries.Count == 0)
        {
            return;
        }

        // Calculate heart rate aggregates
        var heartRates = timeSeries.Where(ts => ts.HeartRateBpm.HasValue)
            .Select(ts => ts.HeartRateBpm!.Value).ToList();
        if (heartRates.Any())
        {
            workout.MaxHeartRateBpm = heartRates.Max();
            workout.AvgHeartRateBpm = (byte)Math.Round(heartRates.Average(x => (double)x));
            workout.MinHeartRateBpm = heartRates.Min();
        }

        // Calculate cadence aggregates
        var cadences = timeSeries.Where(ts => ts.CadenceRpm.HasValue)
            .Select(ts => ts.CadenceRpm!.Value).ToList();
        if (cadences.Any())
        {
            workout.MaxCadenceRpm = cadences.Max();
            workout.AvgCadenceRpm = (byte)Math.Round(cadences.Average(x => (double)x));
        }

        // Calculate power aggregates
        var powers = timeSeries.Where(ts => ts.PowerWatts.HasValue)
            .Select(ts => ts.PowerWatts!.Value).ToList();
        if (powers.Any())
        {
            workout.MaxPowerWatts = powers.Max();
            workout.AvgPowerWatts = (ushort)Math.Round(powers.Average(x => (double)x));
        }
    }

    /// <summary>
    /// Fetches and attaches weather data to a workout.
    /// </summary>
    private async Task FetchAndAttachWeatherAsync(
        Workout workout,
        List<GpxParserService.GpxPoint> trackPoints,
        string? rawStravaDataJson,
        string? rawFitDataJson,
        DateTime startedAtUtc)
    {
        if (trackPoints.Count == 0)
        {
            return;
        }

        var firstPoint = trackPoints[0];
        try
        {
            var weatherJson = await _weatherService.GetWeatherForWorkoutAsync(
                rawStravaDataJson: rawStravaDataJson,
                rawFitDataJson: rawFitDataJson,
                latitude: firstPoint.Latitude,
                longitude: firstPoint.Longitude,
                startTime: startedAtUtc
            );
            if (!string.IsNullOrEmpty(weatherJson))
            {
                workout.Weather = weatherJson;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch weather data for workout from {WorkoutId}", workout.Id);
            // Continue without weather - not a critical error
        }
    }

    /// <summary>
    /// Processes media files for a workout.
    /// </summary>
    public async Task<List<WorkoutMedia>> ProcessMediaFilesAsync(
        Guid workoutId,
        List<string> mediaPaths,
        string tempDir)
    {
        var mediaToAdd = new List<WorkoutMedia>();

        foreach (var mediaPath in mediaPaths)
        {
            try
            {
                // Extract filename from path (e.g., "media/file.jpg" -> "file.jpg")
                var filename = Path.GetFileName(mediaPath);
                
                // Check if media already exists for this workout
                try
                {
                    var mediaExists = await _db.WorkoutMedia
                        .AnyAsync(m => m.WorkoutId == workoutId && m.Filename == filename);
                    
                    if (mediaExists)
                    {
                        _logger.LogInformation("Media file already exists for workout {WorkoutId}: {Filename}", 
                            workoutId, filename);
                        continue;
                    }
                }
                catch (Exception ex) when (ex.Message.Contains("does not exist") || ex.Message.Contains("relation"))
                {
                    // Table doesn't exist yet - skip duplicate check and proceed with import
                    _logger.LogWarning("WorkoutMedia table not found, skipping duplicate check for {Filename}", filename);
                }
                
                // Locate media file in extracted ZIP temp directory
                var mediaFilePath = Path.Combine(tempDir, mediaPath.Replace('/', Path.DirectorySeparatorChar));
                
                // Copy media file and create record
                var mediaRecord = _mediaService.CopyMediaFile(mediaFilePath, workoutId);
                if (mediaRecord != null)
                {
                    mediaToAdd.Add(mediaRecord);
                    _logger.LogInformation("Added media file for workout {WorkoutId}: {MediaPath}", 
                        workoutId, mediaPath);
                }
                else
                {
                    _logger.LogWarning("Failed to process media file for workout: {MediaPath}", mediaPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing media file {MediaPath} for workout {WorkoutId}", 
                    mediaPath, workoutId);
                // Continue processing other media files
            }
        }

        return mediaToAdd;
    }

    /// <summary>
    /// Batch saves workouts, routes, splits, and time-series to the database.
    /// </summary>
    public async Task BatchSaveWorkoutsAsync(
        List<Workout> workouts,
        List<WorkoutRoute> routes,
        List<WorkoutSplit> splits,
        List<WorkoutTimeSeries> timeSeries)
    {
        if (workouts.Count > 0)
        {
            _db.Workouts.AddRange(workouts);
            _db.WorkoutRoutes.AddRange(routes);
            _db.WorkoutSplits.AddRange(splits);
            if (timeSeries.Count > 0)
            {
                _db.WorkoutTimeSeries.AddRange(timeSeries);
            }
            await _db.SaveChangesAsync();
            _logger.LogInformation("Bulk imported {Count} workouts", workouts.Count);
        }
    }

    /// <summary>
    /// Calculates and saves relative effort for a list of workouts.
    /// </summary>
    public async Task CalculateAndSaveRelativeEffortAsync(List<Workout> workouts)
    {
        try
        {
            var settings = await _db.UserSettings.FirstOrDefaultAsync();
            if (settings != null)
            {
                var zones = _zoneService.GetZonesFromUserSettings(settings);
                foreach (var workout in workouts)
                {
                    try
                    {
                        var relativeEffort = _relativeEffortService.CalculateRelativeEffort(workout, zones, _db);
                        if (relativeEffort.HasValue)
                        {
                            workout.RelativeEffort = relativeEffort.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to calculate Relative Effort for workout {WorkoutId}", workout.Id);
                    }
                }
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate Relative Effort for bulk imported workouts");
            // Continue - Relative Effort is optional
        }
    }

    /// <summary>
    /// Result of processing a single activity file.
    /// </summary>
    public class ActivityProcessResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string Action { get; set; } = "created"; // "created", "updated", "skipped"
        public Workout? Workout { get; set; }
        public WorkoutRoute? Route { get; set; }
        public List<WorkoutSplit>? Splits { get; set; }
        public List<WorkoutTimeSeries>? TimeSeries { get; set; }
        public List<string> MediaPaths { get; set; } = new();
    }
}

