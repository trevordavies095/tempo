using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Strava ZIP import: extract, activities.csv, non-run skip, path safety, media copy.
/// Per-file persist is WorkoutIntake.
/// </summary>
public class BulkImportService
{
    private readonly TempoDbContext _db;
    private readonly StravaCsvParserService _csvParser;
    private readonly MediaService _mediaService;
    private readonly WorkoutIntake _workoutIntake;
    private readonly ILogger<BulkImportService> _logger;

    public BulkImportService(
        TempoDbContext db,
        StravaCsvParserService csvParser,
        MediaService mediaService,
        WorkoutIntake workoutIntake,
        ILogger<BulkImportService> logger)
    {
        _db = db;
        _csvParser = csvParser;
        _mediaService = mediaService;
        _workoutIntake = workoutIntake;
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

        var resolvedDestinationDir = Path.GetFullPath(tempDir + Path.DirectorySeparatorChar);

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var entryPath = Path.Combine(tempDir, entry.FullName);
                var resolvedEntryPath = Path.GetFullPath(entryPath);

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
    /// Processes a single activity file via WorkoutIntake.
    /// </summary>
    public async Task<ActivityProcessResult> ProcessActivityFileAsync(
        StravaCsvParserService.StravaActivityRecord activity,
        string tempDir)
    {
        var resolvedDestinationDir = Path.GetFullPath(tempDir + Path.DirectorySeparatorChar);
        var filePath = Path.Combine(tempDir, activity.Filename.Replace('/', Path.DirectorySeparatorChar));
        var resolvedFilePath = Path.GetFullPath(filePath);

        if (!resolvedFilePath.StartsWith(resolvedDestinationDir, StringComparison.Ordinal))
        {
            _logger.LogWarning("Skipping activity file with path traversal attempt: {Filename}", LogSanitizer.Sanitize(activity.Filename));
            return new ActivityProcessResult
            {
                Success = false,
                ErrorMessage = "Invalid file path detected"
            };
        }

        if (!File.Exists(resolvedFilePath))
        {
            return new ActivityProcessResult
            {
                Success = false,
                ErrorMessage = "File not found in ZIP archive"
            };
        }

        if (!resolvedFilePath.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase)
            && !resolvedFilePath.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase)
            && !resolvedFilePath.EndsWith(".fit", StringComparison.OrdinalIgnoreCase))
        {
            return new ActivityProcessResult
            {
                Success = false,
                ErrorMessage = "Unsupported file format. Only .gpx and .fit/.fit.gz files are supported."
            };
        }

        var mediaPaths = !string.IsNullOrWhiteSpace(activity.Media)
            ? activity.Media.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string>();

        try
        {
            await using var stream = File.OpenRead(resolvedFilePath);
            var overlay = new WorkoutIntakeOverlay
            {
                Name = string.IsNullOrWhiteSpace(activity.ActivityName) ? null : activity.ActivityName,
                Notes = BuildNotes(activity),
                RawStravaDataJson = activity.RawStravaDataJson,
                Source = "strava_import"
            };

            var intakeResult = await _workoutIntake.ProcessAsync(
                stream,
                Path.GetFileName(resolvedFilePath),
                overlay);

            if (intakeResult.Action == "error")
            {
                return new ActivityProcessResult
                {
                    Success = false,
                    ErrorMessage = intakeResult.ErrorMessage ?? "Failed to parse file",
                    MediaPaths = mediaPaths
                };
            }

            var workout = intakeResult.Workout;
            if (workout != null
                && (intakeResult.Action == "updated" || intakeResult.Action == "skipped")
                && string.IsNullOrWhiteSpace(workout.Name)
                && !string.IsNullOrWhiteSpace(activity.ActivityName))
            {
                workout.Name = activity.ActivityName;
                await _db.SaveChangesAsync();
            }

            return new ActivityProcessResult
            {
                Success = true,
                Action = intakeResult.Action,
                Workout = workout,
                MediaPaths = mediaPaths
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing activity file {Filename}", activity.Filename);
            return new ActivityProcessResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                MediaPaths = mediaPaths
            };
        }
    }

    private static string? BuildNotes(StravaCsvParserService.StravaActivityRecord activity)
    {
        var notesParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(activity.ActivityDescription))
        {
            notesParts.Add(activity.ActivityDescription);
        }
        if (!string.IsNullOrWhiteSpace(activity.ActivityPrivateNote))
        {
            notesParts.Add(activity.ActivityPrivateNote);
        }
        return notesParts.Count > 0 ? string.Join("\n\n", notesParts) : null;
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
        var resolvedDestinationDir = Path.GetFullPath(tempDir + Path.DirectorySeparatorChar);

        foreach (var mediaPath in mediaPaths)
        {
            try
            {
                var filename = Path.GetFileName(mediaPath);

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
                    _logger.LogWarning("WorkoutMedia table not found, skipping duplicate check for {Filename}", filename);
                }

                var mediaFilePath = Path.Combine(tempDir, mediaPath.Replace('/', Path.DirectorySeparatorChar));
                var resolvedMediaFilePath = Path.GetFullPath(mediaFilePath);

                if (!resolvedMediaFilePath.StartsWith(resolvedDestinationDir, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Skipping media file with path traversal attempt: {MediaPath}", LogSanitizer.Sanitize(mediaPath));
                    continue;
                }

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
            }
        }

        return mediaToAdd;
    }

    /// <summary>
    /// Result of processing a single activity file.
    /// </summary>
    public class ActivityProcessResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string Action { get; set; } = "created";
        public Workout? Workout { get; set; }
        public List<string> MediaPaths { get; set; } = new();
    }
}
