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

        var job = await db.ImportJobs.FindAsync([jobId], stoppingToken);
        if (job == null)
        {
            logger.LogWarning("Import job {JobId} was dequeued but not found", jobId);
            return;
        }

        job.Status = ImportJobStatuses.Running;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(stoppingToken);

        await ApplyUnitPreferenceAsync(db, job.UnitPreference, logger);

        try
        {
            if (string.IsNullOrWhiteSpace(job.ArchivePath) || !File.Exists(job.ArchivePath))
            {
                throw new InvalidOperationException("Import archive is missing");
            }

            await using var zipStream = File.OpenRead(job.ArchivePath);
            var result = await orchestrator.ImportFromZipAsync(zipStream, async progress =>
            {
                ApplyProgress(job, progress);
                await db.SaveChangesAsync(stoppingToken);
            });

            ApplyProgress(job, result);
            job.Status = ImportJobStatuses.Completed;
            job.FinishedAt = DateTime.UtcNow;
            job.ErrorMessage = null;
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import job {JobId} failed", jobId);
            job.Status = ImportJobStatuses.Failed;
            job.ErrorMessage = ex.Message;
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            DeleteArchiveDirectory(job.ArchivePath, logger);
            job.ArchivePath = null;
            await db.SaveChangesAsync(CancellationToken.None);
        }
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

    private static void DeleteArchiveDirectory(string? archivePath, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete import archive at {ArchivePath}", archivePath);
        }
    }
}
