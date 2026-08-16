using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Strava ZIP import orchestration: extract, CSV, per-file intake, media, cleanup.
/// HTTP and the future import-job worker are thin callers.
/// </summary>
public class StravaBulkImportOrchestrator
{
    private readonly BulkImportService _bulkImportService;
    private readonly TempoDbContext _db;
    private readonly ILogger<StravaBulkImportOrchestrator> _logger;

    public StravaBulkImportOrchestrator(
        BulkImportService bulkImportService,
        TempoDbContext db,
        ILogger<StravaBulkImportOrchestrator> logger)
    {
        _bulkImportService = bulkImportService;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Import run activities from a Strava export ZIP stream.
    /// Throws if the archive cannot be extracted or activities.csv is missing.
    /// </summary>
    public async Task<StravaBulkImportResult> ImportFromZipAsync(
        Stream zipStream,
        Func<StravaBulkImportResult, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        string? tempDir = null;
        var result = new StravaBulkImportResult();

        try
        {
            tempDir = _bulkImportService.ExtractZipArchive(zipStream);

            var allActivities = _bulkImportService.ParseActivitiesCsv(tempDir);
            var runActivities = _bulkImportService.GetCsvParser().GetRunActivities(allActivities);
            result.TotalProcessed = runActivities.Count;

            _logger.LogInformation("Found {Total} run activities to process", result.TotalProcessed);
            if (onProgress != null)
            {
                await onProgress(result);
            }

            foreach (var activity in runActivities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var activityResult = await _bulkImportService.ProcessActivityFileAsync(activity, tempDir);

                if (!activityResult.Success)
                {
                    result.ErrorDetails.Add(new StravaBulkImportError
                    {
                        Filename = activity.Filename,
                        Error = activityResult.ErrorMessage
                    });
                }
                else if (activityResult.Action == "skipped")
                {
                    result.Skipped++;
                }
                else if (activityResult.Action == "updated")
                {
                    result.Updated++;
                }
                else
                {
                    result.Successful++;
                }

                if (activityResult.Success
                    && activityResult.Workout != null
                    && activityResult.MediaPaths.Count > 0)
                {
                    var media = await _bulkImportService.ProcessMediaFilesAsync(
                        activityResult.Workout.Id,
                        activityResult.MediaPaths,
                        tempDir);
                    if (media.Count > 0)
                    {
                        _db.WorkoutMedia.AddRange(media);
                        await _db.SaveChangesAsync();
                    }
                }

                if (onProgress != null)
                {
                    await onProgress(result);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing bulk import");
            throw;
        }
        finally
        {
            if (tempDir != null)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temporary directory {TempDir}", tempDir);
                }
            }
        }
    }
}

public class StravaBulkImportResult
{
    public int TotalProcessed { get; set; }
    public int Successful { get; set; }
    public int Skipped { get; set; }
    public int Updated { get; set; }
    public int Errors => ErrorDetails.Count;
    public List<StravaBulkImportError> ErrorDetails { get; set; } = new();
}

public class StravaBulkImportError
{
    public string Filename { get; set; } = string.Empty;
    public string? Error { get; set; }
}
