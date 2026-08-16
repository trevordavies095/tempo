using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public class ImportJobWorker : BackgroundService
{
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
        var orchestrator = scope.ServiceProvider.GetRequiredService<StravaBulkImportOrchestrator>();
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

        job.Status = ImportJobStatuses.Running;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        var unitPreference = job.UnitPreference;
        var archivePath = job.ArchivePath;
        db.Entry(job).State = EntityState.Detached;

        await ApplyUnitPreferenceAsync(db, unitPreference, logger);

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        try
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                throw new InvalidOperationException("Import archive is missing");
            }

            await using var zipStream = File.OpenRead(archivePath);
            var result = await orchestrator.ImportFromZipAsync(zipStream, async progress =>
            {
                var row = await db.ImportJobs.FindAsync([jobId], CancellationToken.None);
                if (row == null)
                {
                    throw new InvalidOperationException("Import job was deleted");
                }

                ApplyProgress(row, progress);
                await db.SaveChangesAsync(CancellationToken.None);
                if (row.CancelRequested)
                {
                    await jobCts.CancelAsync();
                    throw new OperationCanceledException(jobCts.Token);
                }

                db.Entry(row).State = EntityState.Detached;
            }, jobCts.Token);

            var completed = await db.ImportJobs.FindAsync([jobId], CancellationToken.None);
            if (completed == null)
            {
                return;
            }

            ApplyProgress(completed, result);
            if (completed.CancelRequested)
            {
                await FailJobAsync(db, completed, ImportJobErrorMessages.Cancelled, logger);
                return;
            }

            completed.Status = ImportJobStatuses.Completed;
            completed.FinishedAt = DateTime.UtcNow;
            completed.ErrorMessage = null;
            DeleteArchiveDirectory(completed.ArchivePath, completed.Id, logger);
            completed.ArchivePath = null;
            await db.SaveChangesAsync(CancellationToken.None);
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

    private static async Task FailJobAsync(TempoDbContext db, ImportJob job, string message, ILogger logger)
    {
        job.Status = ImportJobStatuses.Failed;
        job.ErrorMessage = message;
        job.FinishedAt = DateTime.UtcNow;
        DeleteArchiveDirectory(job.ArchivePath, job.Id, logger);
        job.ArchivePath = null;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static void ApplyProgress(ImportJob job, StravaBulkImportResult progress)
    {
        job.Total = progress.TotalProcessed;
        job.Successful = progress.Successful;
        job.Skipped = progress.Skipped;
        job.Updated = progress.Updated;
        job.Errors = progress.Errors;
        job.Processed = progress.Successful + progress.Skipped + progress.Updated + progress.Errors;
        job.ErrorDetailsJson = JsonSerializer.Serialize(progress.ErrorDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

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
