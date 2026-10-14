using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public sealed class CoachJobService
{
    private readonly TempoDbContext _db;
    private readonly CoachJobQueue _queue;

    public CoachJobService(TempoDbContext db, CoachJobQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<(CoachJob? Job, string? Error, int StatusCode)> StartGenerateAsync(
        Guid preflightId,
        CancellationToken cancellationToken = default)
    {
        var preflight = await _db.CoachPrivacyPreflights
            .FirstOrDefaultAsync(p => p.Id == preflightId, cancellationToken);
        if (preflight == null)
        {
            return (null, "Preflight not found", 404);
        }

        if (preflight.ConsentedAt == null)
        {
            return (null, "Privacy pre-flight consent is required before generate", 400);
        }

        var provider = await _db.CoachProviderConnections.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (provider == null || provider.ApiKeyCiphertext.Length == 0)
        {
            return (null, "Anthropic provider key is required", 400);
        }

        var committed = await _db.CoachWeeks.AsNoTracking()
            .AnyAsync(
                w => w.WeekStartDate == preflight.WeekStartDate
                     && w.Status == CoachWeekStatuses.Committed,
                cancellationToken);
        if (committed)
        {
            return (null, "Committed weeks are immutable; cannot regenerate", 409);
        }

        var active = await FindActiveJobAsync(cancellationToken);
        if (active != null)
        {
            return (null, "A coach generate job is already active", 409);
        }

        // Drop prior draft for this week so regenerate replaces it.
        var drafts = await _db.CoachWeeks
            .Where(w => w.WeekStartDate == preflight.WeekStartDate && w.Status == CoachWeekStatuses.Draft)
            .ToListAsync(cancellationToken);
        if (drafts.Count > 0)
        {
            _db.CoachWeeks.RemoveRange(drafts);
        }

        var job = new CoachJob
        {
            Status = CoachJobStatuses.Queued,
            WeekStartDate = preflight.WeekStartDate,
            PreflightId = preflight.Id,
            CreatedAt = DateTime.UtcNow
        };
        _db.CoachJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(job.Id, cancellationToken);
        return (job, null, 202);
    }

    public Task<CoachJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.CoachJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<CoachJob?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var active = await FindActiveJobAsync(cancellationToken);
        if (active != null)
        {
            return active;
        }

        return await _db.CoachJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task InterruptIncompleteJobsAsync(CancellationToken cancellationToken = default)
    {
        var incomplete = await _db.CoachJobs
            .Where(j => j.Status == CoachJobStatuses.Queued || j.Status == CoachJobStatuses.Running)
            .ToListAsync(cancellationToken);
        if (incomplete.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var job in incomplete)
        {
            job.Status = CoachJobStatuses.Failed;
            job.ErrorCode = CoachJobErrorCodes.Interrupted;
            job.ErrorMessage = "interrupted";
            job.CompletedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private Task<CoachJob?> FindActiveJobAsync(CancellationToken cancellationToken)
        => _db.CoachJobs.AsNoTracking()
            .Where(j => j.Status == CoachJobStatuses.Queued || j.Status == CoachJobStatuses.Running)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
