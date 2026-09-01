using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Import-job module: create, chunks, complete, adapter accept, current, cancel,
/// one-active-job, stale receiving replace, archive cleanup, and worker wakeup.
/// </summary>
public class ImportJobService
{
    private readonly TempoDbContext _db;
    private readonly MediaStorageConfig _mediaStorage;
    private readonly ImportJobQueue _queue;
    private readonly ILogger<ImportJobService> _logger;

    public ImportJobService(
        TempoDbContext db,
        MediaStorageConfig mediaStorage,
        ImportJobQueue queue,
        ILogger<ImportJobService> logger)
    {
        _db = db;
        _mediaStorage = mediaStorage;
        _queue = queue;
        _logger = logger;
    }

    public async Task<ImportJobHttpResult> CreateReceivingAsync(CreateImportJobRequest request)
    {
        var validation = ValidateCreate(request.Kind, request.Filename, request.ByteSize, request.UnitPreference);
        if (validation != null)
        {
            return validation;
        }

        var blocked = await TryReplaceOrConflictAsync();
        if (blocked != null)
        {
            return blocked;
        }

        var job = NewJob(
            ImportJobStatuses.Receiving,
            request.Kind,
            request.Filename,
            request.ByteSize,
            0,
            request.UnitPreference);
        Directory.CreateDirectory(JobDirectory(job.Id));
        _db.ImportJobs.Add(job);
        await _db.SaveChangesAsync();
        return ImportJobHttpResult.Json(StatusCodes.Status201Created, job);
    }

    public async Task<ImportJobHttpResult> PutChunkAsync(Guid id, int index, int total, Stream body)
    {
        var job = await _db.ImportJobs.FindAsync(id);
        if (job == null)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status404NotFound, "Import job not found");
        }

        if (job.Status != ImportJobStatuses.Receiving)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Job is not receiving chunks");
        }

        var expectedTotal = ExpectedChunkCount(job.ByteSize);
        if (total != expectedTotal || index < 0 || index >= expectedTotal)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Chunk index or total is invalid");
        }

        await using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Chunk is empty");
        }

        if (index < expectedTotal - 1 && bytes.Length != ImportJobLimits.ChunkSizeBytes)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Chunk size is invalid");
        }

        var expectedIndex = ReceivedChunkCount(job);
        if (index != expectedIndex)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Chunks must be uploaded sequentially");
        }

        var chunkDir = ChunkDirectory(job.Id);
        Directory.CreateDirectory(chunkDir);
        await File.WriteAllBytesAsync(ChunkPath(job.Id, index), bytes);

        job.BytesReceived += bytes.Length;
        job.LastChunkAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ImportJobHttpResult.Json(StatusCodes.Status200OK, job);
    }

    public async Task<ImportJobHttpResult> CompleteAsync(Guid id)
    {
        var job = await _db.ImportJobs.FindAsync(id);
        if (job == null)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status404NotFound, "Import job not found");
        }

        if (job.Status != ImportJobStatuses.Receiving)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Job is not receiving chunks");
        }

        var expectedTotal = ExpectedChunkCount(job.ByteSize);
        for (var i = 0; i < expectedTotal; i++)
        {
            if (!File.Exists(ChunkPath(job.Id, i)))
            {
                return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Not all chunk indexes are present");
            }
        }

        var jobDir = JobDirectory(job.Id);
        Directory.CreateDirectory(jobDir);
        var archivePath = ArchiveFilePath(job.Id);
        await AssembleChunksAsync(job.Id, expectedTotal, archivePath);

        var assembledLength = new FileInfo(archivePath).Length;
        if (assembledLength != job.ByteSize)
        {
            TryDeleteFile(archivePath);
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Assembled archive size does not match byteSize");
        }

        job.BytesReceived = assembledLength;
        job.ArchivePath = archivePath;
        job.Status = ImportJobStatuses.Queued;
        await _db.SaveChangesAsync();
        await _queue.EnqueueAsync(job.Id);
        return ImportJobHttpResult.Json(StatusCodes.Status202Accepted, job);
    }

    public async Task<ImportJobHttpResult> AcceptWholeArchiveAsync(
        Stream zipStream,
        string filename,
        long byteSize,
        string? unitPreference,
        string kind)
    {
        var validation = ValidateCreate(kind, filename, byteSize, unitPreference);
        if (validation != null)
        {
            return validation;
        }

        var blocked = await TryReplaceOrConflictAsync();
        if (blocked != null)
        {
            return blocked;
        }

        var job = NewJob(
            ImportJobStatuses.Queued,
            kind,
            filename,
            byteSize,
            byteSize,
            unitPreference);
        var jobDir = JobDirectory(job.Id);
        Directory.CreateDirectory(jobDir);
        var archivePath = ArchiveFilePath(job.Id);
        job.ArchivePath = archivePath;

        try
        {
            await using (var archive = File.Create(archivePath))
            {
                await zipStream.CopyToAsync(archive);
            }

            job.BytesReceived = new FileInfo(archivePath).Length;
            job.ByteSize = job.BytesReceived;
            _db.ImportJobs.Add(job);
            await _db.SaveChangesAsync();
            await _queue.EnqueueAsync(job.Id);
            return ImportJobHttpResult.Json(StatusCodes.Status202Accepted, job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept import upload");
            DeleteJobDirectory(job.Id);
            return ImportJobHttpResult.Fail(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    public async Task<ImportJobHttpResult> GetAsync(Guid id)
    {
        var job = await _db.ImportJobs.FindAsync(id);
        if (job == null)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status404NotFound, "Import job not found");
        }

        return ImportJobHttpResult.Json(StatusCodes.Status200OK, job);
    }

    public async Task<ImportJobHttpResult> GetCurrentAsync()
    {
        var job = await FindActiveJobAsync();
        if (job == null)
        {
            return ImportJobHttpResult.NoContent();
        }

        return ImportJobHttpResult.Json(StatusCodes.Status200OK, job);
    }

    public async Task<ImportJobHttpResult> CancelAsync(Guid id)
    {
        var job = await _db.ImportJobs.FindAsync(id);
        if (job == null)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status404NotFound, "Import job not found");
        }

        if (job.Status is not (ImportJobStatuses.Receiving or ImportJobStatuses.Queued or ImportJobStatuses.Running))
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Job cannot be cancelled");
        }

        if (job.Status is ImportJobStatuses.Receiving or ImportJobStatuses.Queued)
        {
            job.CancelRequested = true;
            FailCancelled(job);
            await _db.SaveChangesAsync();
            DeleteJobDirectory(job.Id);
            job.ArchivePath = null;
            await _db.SaveChangesAsync();
        }
        else
        {
            var updated = await _db.ImportJobs
                .Where(row => row.Id == id && row.Status == ImportJobStatuses.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.CancelRequested, true));

            if (updated == 0)
            {
                return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "Job cannot be cancelled");
            }

            await _db.Entry(job).ReloadAsync();
        }

        return ImportJobHttpResult.Json(StatusCodes.Status200OK, job);
    }

    public async Task InterruptIncompleteJobsAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await _db.ImportJobs
            .Where(j => j.Status == ImportJobStatuses.Queued || j.Status == ImportJobStatuses.Running)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            job.Status = ImportJobStatuses.Failed;
            job.ErrorMessage = ImportJobErrorMessages.Interrupted;
            job.FinishedAt = DateTime.UtcNow;
            job.CancelRequested = true;
            DeleteJobDirectory(job.Id);
            job.ArchivePath = null;
        }

        if (jobs.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public static int ExpectedChunkCount(long byteSize)
    {
        if (byteSize <= 0)
        {
            return 0;
        }

        return (int)((byteSize + ImportJobLimits.ChunkSizeBytes - 1) / ImportJobLimits.ChunkSizeBytes);
    }

    private async Task<ImportJobHttpResult?> TryReplaceOrConflictAsync()
    {
        var active = await FindActiveJobAsync();
        if (active == null)
        {
            return null;
        }

        if (active.Status == ImportJobStatuses.Receiving && IsStaleReceiving(active))
        {
            active.Status = ImportJobStatuses.Failed;
            active.ErrorMessage = "replaced";
            active.FinishedAt = DateTime.UtcNow;
            DeleteJobDirectory(active.Id);
            active.ArchivePath = null;
            await _db.SaveChangesAsync();
            return null;
        }

        return ImportJobHttpResult.Json(StatusCodes.Status409Conflict, active);
    }

    private async Task<ImportJob?> FindActiveJobAsync()
    {
        return await _db.ImportJobs
            .Where(j =>
                j.Status == ImportJobStatuses.Receiving ||
                j.Status == ImportJobStatuses.Queued ||
                j.Status == ImportJobStatuses.Running)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private static bool IsStaleReceiving(ImportJob job)
    {
        var last = job.LastChunkAt ?? job.CreatedAt;
        return DateTime.UtcNow - last >= ImportJobLimits.StaleReceivingAfter;
    }

    private ImportJob NewJob(
        string status,
        string kind,
        string filename,
        long byteSize,
        long bytesReceived,
        string? unitPreference)
    {
        var now = DateTime.UtcNow;
        var job = new ImportJob
        {
            Kind = kind,
            Status = status,
            Filename = Path.GetFileName(filename),
            ByteSize = byteSize,
            BytesReceived = bytesReceived,
            UnitPreference = NormalizeUnitPreference(unitPreference),
            CreatedAt = now,
            LastChunkAt = now
        };
        job.ArchivePath = ArchiveFilePath(job.Id);
        return job;
    }

    private ImportJobHttpResult? ValidateCreate(string kind, string filename, long byteSize, string? unitPreference)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "kind is required");
        }

        if (kind is not (ImportJobKinds.StravaBulk or ImportJobKinds.TempoExport))
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "kind must be strava_bulk or tempo_export");
        }

        if (kind == ImportJobKinds.TempoExport && !string.IsNullOrWhiteSpace(unitPreference))
        {
            return ImportJobHttpResult.Fail(
                StatusCodes.Status400BadRequest,
                "unitPreference is not allowed for tempo_export");
        }

        if (string.IsNullOrWhiteSpace(filename) || !filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "File must be a ZIP file");
        }

        if (byteSize <= 0)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "byteSize must be greater than 0");
        }

        if (byteSize > ImportJobLimits.MaxByteSize)
        {
            return ImportJobHttpResult.Fail(StatusCodes.Status400BadRequest, "File exceeds the 500MB limit");
        }

        return null;
    }

    private static string NormalizeUnitPreference(string? unitPreference)
    {
        if (string.IsNullOrWhiteSpace(unitPreference))
        {
            return "metric";
        }

        return unitPreference;
    }

    private string JobDirectory(Guid id) => Path.Combine(_mediaStorage.RootPath, "imports", id.ToString());

    private string ChunkDirectory(Guid id) => Path.Combine(JobDirectory(id), "chunks");

    private string ChunkPath(Guid id, int index) => Path.Combine(ChunkDirectory(id), $"{index:D8}.part");

    private string ArchiveFilePath(Guid id) => Path.Combine(JobDirectory(id), "archive.zip");

    private int ReceivedChunkCount(ImportJob job)
    {
        var dir = ChunkDirectory(job.Id);
        if (!Directory.Exists(dir))
        {
            return 0;
        }

        return Directory.GetFiles(dir, "*.part").Length;
    }

    private async Task AssembleChunksAsync(Guid id, int total, string archivePath)
    {
        await using var output = File.Create(archivePath);
        for (var i = 0; i < total; i++)
        {
            await using var input = File.OpenRead(ChunkPath(id, i));
            await input.CopyToAsync(output);
        }
    }

    private void DeleteJobDirectory(Guid id)
    {
        var dir = JobDirectory(id);
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete import archive directory {JobId}", id);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Leave the incomplete assemble; complete will not enqueue.
        }
    }

    private static void FailCancelled(ImportJob job)
    {
        job.Status = ImportJobStatuses.Failed;
        job.ErrorMessage = ImportJobErrorMessages.Cancelled;
        job.FinishedAt = DateTime.UtcNow;
    }
}

public sealed class ImportJobHttpResult
{
    public int StatusCode { get; init; }
    public ImportJobDocument? Document { get; init; }
    public string? Error { get; init; }

    public static ImportJobHttpResult Json(int statusCode, ImportJob job) =>
        new() { StatusCode = statusCode, Document = ImportJobDocument.FromEntity(job) };

    public static ImportJobHttpResult Fail(int statusCode, string error) =>
        new() { StatusCode = statusCode, Error = error };

    public static ImportJobHttpResult NoContent() =>
        new() { StatusCode = StatusCodes.Status204NoContent };

    public IResult ToHttpResult()
    {
        if (StatusCode == StatusCodes.Status204NoContent)
        {
            return Results.NoContent();
        }

        if (Document != null)
        {
            return Results.Json(Document, statusCode: StatusCode);
        }

        if (StatusCode >= 500)
        {
            return Results.Problem(detail: Error, statusCode: StatusCode, title: "Error accepting bulk import");
        }

        return Results.Json(new { error = Error }, statusCode: StatusCode);
    }
}
