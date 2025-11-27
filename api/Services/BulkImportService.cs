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

            // Fetch weather data
            await FetchAndAttachWeatherAsync(workout, trackPoints, activity.RawStravaDataJson, fitResult?.RawFitDataJson, startedAtUtc);

            return new ActivityProcessResult
            {
                Success = true,
                Action = "created",
                Workout = workout,
                Route = route,
                Splits = splits,
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
    /// Batch saves workouts, routes, and splits to the database.
    /// </summary>
    public async Task BatchSaveWorkoutsAsync(
        List<Workout> workouts,
        List<WorkoutRoute> routes,
        List<WorkoutSplit> splits)
    {
        if (workouts.Count > 0)
        {
            _db.Workouts.AddRange(workouts);
            _db.WorkoutRoutes.AddRange(routes);
            _db.WorkoutSplits.AddRange(splits);
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
        public List<string> MediaPaths { get; set; } = new();
    }
}

