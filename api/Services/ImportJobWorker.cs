using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public class ImportJobWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ImportJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportJobWorker> _logger;

    public ImportJobWorker(
        ImportJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ImportJobWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var importJobs = scope.ServiceProvider.GetRequiredService<ImportJobService>();
            await importJobs.InterruptIncompleteJobsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark interrupted import jobs on startup");
        }

        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing import job {JobId}", jobId);
            }
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ImportJobWorker>>();

        var job = await db.ImportJobs.FindAsync([jobId], CancellationToken.None);
        if (job == null)
        {
            logger.LogWarning("Import job {JobId} was dequeued but not found", jobId);
            return;
        }

        if (job.Status != ImportJobStatuses.Queued)
        {
            DeleteArchiveDirectory(job.ArchivePath, job.Id, logger);
            if (job.ArchivePath != null)
            {
                job.ArchivePath = null;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            return;
        }

        if (job.CancelRequested)
        {
            await FailJobAsync(db, job, ImportJobErrorMessages.Cancelled, logger);
            return;
        }

        if (job.Kind is not (ImportJobKinds.StravaBulk or ImportJobKinds.TempoExport))
        {
            await FailJobAsync(db, job, $"Unsupported import kind: {job.Kind}", logger);
            return;
        }

        var kind = job.Kind;
        job.Status = ImportJobStatuses.Running;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        var unitPreference = job.UnitPreference;
        var archivePath = job.ArchivePath;
        db.Entry(job).State = EntityState.Detached;

        if (kind == ImportJobKinds.StravaBulk)
        {
            await ApplyUnitPreferenceAsync(db, unitPreference, logger);
        }

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        try
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                throw new InvalidOperationException("Import archive is missing");
            }

            await using var zipStream = File.OpenRead(archivePath);

            if (kind == ImportJobKinds.StravaBulk)
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<StravaBulkImportOrchestrator>();
                var result = await orchestrator.ImportFromZipAsync(zipStream, async progress =>
                {
                    await PersistJobProgressAsync(jobId, jobCts, row => ApplyStravaProgress(row, progress));
                }, jobCts.Token);

                var completed = await db.ImportJobs.FindAsync([jobId], CancellationToken.None);
                if (completed == null)
                {
                    return;
                }

                ApplyStravaProgress(completed, result);
                if (await IsCancelRequestedAsync(db, jobId))
                {
                    await FailJobAsync(db, completed, ImportJobErrorMessages.Cancelled, logger);
                    return;
                }

                await CompleteJobAsync(db, completed, logger);
            }
            else
            {
                var importService = scope.ServiceProvider.GetRequiredService<ImportService>();
                var result = await importService.ImportExportAsync(zipStream, async progress =>
                {
                    await PersistJobProgressAsync(jobId, jobCts, row => ApplyTempoProgress(row, progress));
                }, jobCts.Token);

                var completed = await db.ImportJobs.FindAsync([jobId], CancellationToken.None);
                if (completed == null)
                {
                    return;
                }

                ApplyTempoProgressFromResult(completed, result, completed.Total);

                if (await IsCancelRequestedAsync(db, jobId))
                {
                    await FailJobAsync(db, completed, ImportJobErrorMessages.Cancelled, logger);
                    return;
                }

                await CompleteJobAsync(db, completed, logger);
            }
        }
        catch (OperationCanceledException)
        {
            var cancelled = await db.ImportJobs.FindAsync([jobId], CancellationToken.None);
            if (cancelled == null)
            {
                return;
            }

            var message = cancelled.CancelRequested
                ? ImportJobErrorMessages.Cancelled
                : ImportJobErrorMessages.Interrupted;
            await FailJobAsync(db, cancelled, message, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import job {JobId} failed", jobId);
            var failed = await db.ImportJobs.FindAsync([jobId], CancellationToken.None);
            if (failed == null)
            {
                return;
            }

            await FailJobAsync(db, failed, ex.Message, logger);
        }
    }

    /// <summary>
    /// Persist job progress on a separate DbContext so SaveChanges does not flush
    /// pending bulk-import entities from the worker's main scope.
    /// </summary>
    private async Task PersistJobProgressAsync(Guid jobId, CancellationTokenSource jobCts, Action<ImportJob> apply)
    {
        using var progressScope = _scopeFactory.CreateScope();
        var progressDb = progressScope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var row = await progressDb.ImportJobs.FindAsync([jobId], CancellationToken.None);
        if (row == null)
        {
            throw new InvalidOperationException("Import job was deleted");
        }

        apply(row);
        await progressDb.SaveChangesAsync(CancellationToken.None);
        if (row.CancelRequested)
        {
            await jobCts.CancelAsync();
            throw new OperationCanceledException(jobCts.Token);
        }
    }

    private static async Task<bool> IsCancelRequestedAsync(TempoDbContext db, Guid jobId) =>
        await db.ImportJobs
            .AsNoTracking()
            .Where(row => row.Id == jobId)
            .Select(row => row.CancelRequested)
            .SingleAsync(CancellationToken.None);

    private static async Task CompleteJobAsync(TempoDbContext db, ImportJob job, ILogger logger)
    {
        var archivePath = job.ArchivePath;

        // Read cancel state from the database so a concurrent cancel is not clobbered
        // when persisting final counters from the tracked entity.
        if (await IsCancelRequestedAsync(db, job.Id))
        {
            await FailJobAsync(db, job, ImportJobErrorMessages.Cancelled, logger);
            return;
        }

        // Persist final counters/results first, then only mark the job completed if no
        // concurrent cancel request has landed. This avoids a race where a late cancel
        // can be overwritten by the final completed write.
        db.Entry(job).Property(row => row.CancelRequested).IsModified = false;
        await db.SaveChangesAsync(CancellationToken.None);

        if (await IsCancelRequestedAsync(db, job.Id))
        {
            await FailJobAsync(db, job, ImportJobErrorMessages.Cancelled, logger);
            return;
        }

        var finishedAt = DateTime.UtcNow;
        var updated = await db.ImportJobs
            .Where(row => row.Id == job.Id && !row.CancelRequested)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, ImportJobStatuses.Completed)
                .SetProperty(row => row.FinishedAt, finishedAt)
                .SetProperty(row => row.ErrorMessage, (string?)null)
                .SetProperty(row => row.ArchivePath, (string?)null), CancellationToken.None);

        if (updated == 0)
        {
            await db.Entry(job).ReloadAsync(CancellationToken.None);
            await FailJobAsync(db, job, ImportJobErrorMessages.Cancelled, logger);
            return;
        }

        DeleteArchiveDirectory(archivePath, job.Id, logger);
    }

    private static async Task FailJobAsync(TempoDbContext db, ImportJob job, string message, ILogger logger)
    {
        job.Status = ImportJobStatuses.Failed;
        job.ErrorMessage = message;
        job.FinishedAt = DateTime.UtcNow;
        DeleteArchiveDirectory(job.ArchivePath, job.Id, logger);
        job.ArchivePath = null;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static void ApplyStravaProgress(ImportJob job, StravaBulkImportResult progress)
    {
        job.Total = progress.TotalProcessed;
        job.Successful = progress.Successful;
        job.Skipped = progress.Skipped;
        job.Updated = progress.Updated;
        job.Errors = progress.Errors;
        job.Processed = progress.Successful + progress.Skipped + progress.Updated + progress.Errors;
        job.ErrorDetailsJson = JsonSerializer.Serialize(progress.ErrorDetails, JsonOptions);
    }

    private static void ApplyTempoProgress(ImportJob job, TempoExportProgress progress)
    {
        job.Total = progress.Total;
        job.Processed = progress.Processed;
        ApplyTempoProgressFromResult(job, progress.Snapshot, progress.Total);
    }

    private static void ApplyTempoProgressFromResult(
        ImportJob job,
        ImportService.ImportResult result,
        int? total)
    {
        if (total.HasValue)
        {
            job.Total = total.Value;
        }

        var stats = result.Statistics;
        job.Successful = SumImported(stats);
        job.Skipped = SumSkipped(stats);
        job.Errors = SumErrors(stats);
        job.Updated = 0;
        job.Processed = job.Successful + job.Skipped + job.Errors;
        job.ErrorDetailsJson = null;
        job.ResultJson = JsonSerializer.Serialize(new ImportJobResultPayload
        {
            Statistics = ToJobStatistics(stats),
            Warnings = result.Warnings,
            Errors = result.Errors
        }, JsonOptions);
    }

    private static int SumImported(ImportService.ImportStatistics s) =>
        s.Settings.Imported + s.Shoes.Imported + s.Workouts.Imported + s.Routes.Imported
        + s.Splits.Imported + s.TimeSeries.Imported + s.Media.Imported + s.BestEfforts.Imported
        + s.RawFiles.Imported;

    private static int SumSkipped(ImportService.ImportStatistics s) =>
        s.Settings.Skipped + s.Shoes.Skipped + s.Workouts.Skipped + s.Routes.Skipped
        + s.Splits.Skipped + s.TimeSeries.Skipped + s.Media.Skipped + s.BestEfforts.Skipped
        + s.RawFiles.Skipped;

    private static int SumErrors(ImportService.ImportStatistics s) =>
        s.Settings.Errors + s.Shoes.Errors + s.Workouts.Errors + s.Routes.Errors
        + s.Splits.Errors + s.TimeSeries.Errors + s.Media.Errors + s.BestEfforts.Errors
        + s.RawFiles.Errors;

    private static ImportJobStatistics ToJobStatistics(ImportService.ImportStatistics s) => new()
    {
        Settings = MapItem(s.Settings),
        Shoes = MapItem(s.Shoes),
        Workouts = MapItem(s.Workouts),
        Routes = MapItem(s.Routes),
        Splits = MapItem(s.Splits),
        TimeSeries = MapItem(s.TimeSeries),
        Media = MapItem(s.Media),
        BestEfforts = MapItem(s.BestEfforts),
        RawFiles = MapItem(s.RawFiles)
    };

    private static ImportJobItemStatistics MapItem(ImportService.ItemStatistics item) => new()
    {
        Imported = item.Imported,
        Skipped = item.Skipped,
        Errors = item.Errors
    };

    public static async Task ApplyUnitPreferenceAsync(
        TempoDbContext db,
        string? unitPreference,
        ILogger logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(unitPreference) ||
                (unitPreference != "metric" && unitPreference != "imperial"))
            {
                return;
            }

            var settings = await db.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new UserSettings();
                db.UserSettings.Add(settings);
            }

            if (settings.UnitPreference != unitPreference)
            {
                settings.UnitPreference = unitPreference;
                settings.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                logger.LogInformation("Updated unit preference to {UnitPreference}", Utils.LogSanitizer.Sanitize(unitPreference));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save unit preference to UserSettings");
        }
    }

    private static void DeleteArchiveDirectory(string? archivePath, Guid jobId, ILogger logger)
    {
        try
        {
            string? dir = null;
            if (!string.IsNullOrWhiteSpace(archivePath))
            {
                dir = Path.GetDirectoryName(archivePath);
            }

            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return;
            }

            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete import archive for job {JobId}", jobId);
        }
    }
}
