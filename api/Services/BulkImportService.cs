using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Service for bulk importing workouts from Strava export ZIP files.
/// </summary>
public class BulkImportService
{
    private readonly TempoDbContext _db;
    private readonly StravaCsvParserService _csvParser;
    private readonly MediaService _mediaService;
    private readonly HeartRateZoneService _zoneService;
    private readonly RelativeEffortService _relativeEffortService;
    private readonly WorkoutImportPipeline _importPipeline;
    private readonly ILogger<BulkImportService> _logger;

    public BulkImportService(
        TempoDbContext db,
        StravaCsvParserService csvParser,
        MediaService mediaService,
        HeartRateZoneService zoneService,
        RelativeEffortService relativeEffortService,
        WorkoutImportPipeline importPipeline,
        ILogger<BulkImportService> logger)
    {
        _db = db;
        _csvParser = csvParser;
        _mediaService = mediaService;
        _zoneService = zoneService;
        _relativeEffortService = relativeEffortService;
        _importPipeline = importPipeline;
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

        // Get the fully resolved destination directory path for validation
        var resolvedDestinationDir = Path.GetFullPath(tempDir + Path.DirectorySeparatorChar);

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                // Skip directory entries (they end with /)
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                // Construct the raw output path
                var entryPath = Path.Combine(tempDir, entry.FullName);
                
                // Resolve any directory traversal elements (e.g., ..) in the path
                var resolvedEntryPath = Path.GetFullPath(entryPath);

                // Validate that the resolved path is within the destination directory
                // This prevents directory traversal attacks
                if (!resolvedEntryPath.StartsWith(resolvedDestinationDir, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Skipping zip entry with path traversal attempt: {EntryFullName}", LogSanitizer.Sanitize(entry.FullName));
                    continue;
                }

                var entryDir = Path.GetDirectoryName(resolvedEntryPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                using (var entryStream = entry.Open())
                using (var fileStream = new FileStream(resolvedEntryPath, FileMode.Create))
                {
                    entryStream.CopyTo(fileStream);
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
        var resolvedDestinationDir = Path.GetFullPath(tempDir + Path.DirectorySeparatorChar);
        var filePath = Path.Combine(tempDir, activity.Filename.Replace('/', Path.DirectorySeparatorChar));
        var resolvedFilePath = Path.GetFullPath(filePath);
        
        // Validate that the resolved path is within the destination directory
        // This prevents directory traversal attacks
        if (!resolvedFilePath.StartsWith(resolvedDestinationDir, StringComparison.Ordinal))
        {
            _logger.LogWarning("Skipping activity file with path traversal attempt: {Filename}", LogSanitizer.Sanitize(activity.Filename));
            return new ActivityProcessResult { Success = false, ErrorMessage = "Invalid file path detected" };
        }

        if (!File.Exists(resolvedFilePath))
            return new ActivityProcessResult { Success = false, ErrorMessage = "File not found in ZIP archive" };

        try
        {
            byte[] rawFileData;
            using (var fileStream = File.OpenRead(resolvedFilePath))
            using (var ms = new MemoryStream())
            {
                await fileStream.CopyToAsync(ms);
                rawFileData = ms.ToArray();
            }

            var notesParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(activity.ActivityDescription))
                notesParts.Add(activity.ActivityDescription);
            if (!string.IsNullOrWhiteSpace(activity.ActivityPrivateNote))
                notesParts.Add(activity.ActivityPrivateNote);
            var notes = notesParts.Count > 0 ? string.Join("\n\n", notesParts) : null;

            var pipelineOptions = new WorkoutImportPipeline.ImportOptions(
                SplitDistanceMeters: splitDistanceMeters,
                ExplicitSource:      "strava_import",
                Name:                !string.IsNullOrWhiteSpace(activity.ActivityName) ? activity.ActivityName : null,
                Notes:               notes,
                RawStravaDataJson:   activity.RawStravaDataJson);

            var result = await _importPipeline.RunAsync(
                new WorkoutImportPipeline.ImportInput(rawFileData, Path.GetFileName(activity.Filename), pipelineOptions));

            var mediaPaths = ParseMediaPaths(activity.Media);

            switch (result)
            {
                case WorkoutImportPipeline.Skipped skipped:
                    return new ActivityProcessResult
                    {
                        Success = true, Action = "skipped",
                        Workout = await _db.Workouts.FindAsync(skipped.ExistingWorkoutId),
                        MediaPaths = mediaPaths
                    };

                case WorkoutImportPipeline.Updated updated:
                    return new ActivityProcessResult
                    {
                        Success = true, Action = "updated",
                        Workout = updated.ExistingWorkout,
                        MediaPaths = mediaPaths
                    };

                case WorkoutImportPipeline.Created created:
                    return new ActivityProcessResult
                    {
                        Success = true, Action = "created",
                        Workout   = created.Workout,
                        Route     = created.Route,
                        Splits    = created.Splits,
                        TimeSeries = created.TimeSeries.Count > 0 ? created.TimeSeries : null,
                        MediaPaths = mediaPaths
                    };

                default:
                    return new ActivityProcessResult { Success = false, ErrorMessage = "Unexpected pipeline result" };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing activity file {Filename}", activity.Filename);
            return new ActivityProcessResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static List<string> ParseMediaPaths(string? media) =>
        !string.IsNullOrWhiteSpace(media)
            ? media.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string>();


    /// <summary>
    /// Processes media files for a workout.
    /// </summary>
    public async Task<List<WorkoutMedia>> ProcessMediaFilesAsync(
        Guid workoutId,
        List<string> mediaPaths,
        string tempDir)
    {
        var mediaToAdd = new List<WorkoutMedia>();

        // Get the fully resolved destination directory path for validation
        var resolvedDestinationDir = Path.GetFullPath(tempDir + Path.DirectorySeparatorChar);

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
                
                // Resolve any directory traversal elements (e.g., ..) in the path
                var resolvedMediaFilePath = Path.GetFullPath(mediaFilePath);
                
                // Validate that the resolved path is within the destination directory
                // This prevents directory traversal attacks
                if (!resolvedMediaFilePath.StartsWith(resolvedDestinationDir, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Skipping media file with path traversal attempt: {MediaPath}", LogSanitizer.Sanitize(mediaPath));
                    continue;
                }
                
                // Copy media file and create record
                var mediaRecord = _mediaService.CopyMediaFile(resolvedMediaFilePath, workoutId);
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

