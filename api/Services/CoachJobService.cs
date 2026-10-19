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

        if (preflight.Kind == CoachPrivacyPreflightKinds.Exchange)
        {
            return (null, "Exchange prefights cannot start a ritual generate job", 400);
        }

        var provider = await _db.CoachProviderConnections.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (provider == null || provider.ApiKeyCiphertext.Length == 0)
        {
            return (null, "Anthropic provider key is required", 400);
        }

        var startedAdjust = await _db.CoachAdjustments.AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.WeekStartDate == preflight.WeekStartDate
                     && a.Status == CoachAdjustmentStatuses.Started,
                cancellationToken);

        // Ritual generate: require persisted check-in for week N (plan Monday − 7). Adjust skips the gate.
        if (startedAdjust == null)
        {
            var checkInMonday = preflight.WeekStartDate.AddDays(-7);
            var hasCheckIn = await _db.CoachCheckIns.AsNoTracking()
                .AnyAsync(c => c.WeekStartDate == checkInMonday, cancellationToken);
            if (!hasCheckIn)
            {
                return (
                    null,
                    $"Save a Coach check-in for {checkInMonday:yyyy-MM-dd} before generating the plan for {preflight.WeekStartDate:yyyy-MM-dd}.",
                    400);
            }
        }

        var committed = await _db.CoachWeeks.AsNoTracking()
            .AnyAsync(
                w => w.WeekStartDate == preflight.WeekStartDate
                     && w.Status == CoachWeekStatuses.Committed,
                cancellationToken);
        if (committed && startedAdjust == null)
        {
            return (null, "Committed weeks are immutable; cannot regenerate", 409);
        }

        if (committed && startedAdjust != null)
        {
            // Adjust generate requires the preflight packet to include the adjustment context.
            if (!preflight.RedactedPayloadJson.Contains("\"adjustment\":", StringComparison.OrdinalIgnoreCase)
                && !preflight.RedactedPayloadJson.Contains("\"Adjustment\":", StringComparison.Ordinal))
            {
                return (null, "Privacy pre-flight must be re-assembled after starting Adjust", 400);
            }
        }

        var activeRitual = await FindActiveRitualJobAsync(cancellationToken);
        if (activeRitual != null)
        {
            return (null, "A coach generate job is already active", 409);
        }

        // Drop prior draft for this week so regenerate/adjust replaces it.
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
            Kind = startedAdjust != null ? CoachJobKinds.Adjust : CoachJobKinds.Generate,
            AdjustmentId = startedAdjust?.Id,
            WeekStartDate = preflight.WeekStartDate,
            PreflightId = preflight.Id,
            CreatedAt = DateTime.UtcNow
        };
        _db.CoachJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(job.Id, cancellationToken);
        return (job, null, 202);
    }

    public async Task<(CoachJob? Job, string? Error, int StatusCode)> StartExchangeAsync(
        Guid preflightId,
        Guid exchangeId,
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
            return (null, "Privacy pre-flight consent is required before exchange", 400);
        }

        if (preflight.Kind != CoachPrivacyPreflightKinds.Exchange)
        {
            return (null, "Preflight must be an exchange kind", 400);
        }

        var exchange = await _db.CoachExchanges
            .FirstOrDefaultAsync(e => e.Id == exchangeId, cancellationToken);
        if (exchange == null)
        {
            return (null, "Exchange not found", 404);
        }

        var provider = await _db.CoachProviderConnections.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (provider == null || provider.ApiKeyCiphertext.Length == 0)
        {
            return (null, "Anthropic provider key is required", 400);
        }

        var activeExchange = await FindActiveExchangeJobAsync(cancellationToken);
        if (activeExchange != null)
        {
            return (null, "A coach exchange job is already active", 409);
        }

        var job = new CoachJob
        {
            Status = CoachJobStatuses.Queued,
            Kind = CoachJobKinds.Exchange,
            WeekStartDate = preflight.WeekStartDate,
            PreflightId = preflight.Id,
            CreatedAt = DateTime.UtcNow
        };
        _db.CoachJobs.Add(job);
        exchange.JobId = job.Id;
        exchange.PreflightId = preflight.Id;
        exchange.Status = CoachExchangeStatuses.Queued;
        exchange.ErrorCode = null;
        exchange.ErrorMessage = null;
        exchange.CompletedAt = null;
        exchange.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(job.Id, cancellationToken);
        return (job, null, 202);
    }

    public Task<CoachJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.CoachJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public Task<CoachJob?> GetActiveJobAsync(CancellationToken cancellationToken = default) =>
        FindActiveRitualJobAsync(cancellationToken);

    /// <summary>
    /// Ritual card current job: active ritual, else latest ritual (never exchange).
    /// </summary>
    public async Task<CoachJob?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var active = await FindActiveRitualJobAsync(cancellationToken);
        if (active != null)
        {
            return active;
        }

        return await _db.CoachJobs.AsNoTracking()
            .Where(j => j.Kind != CoachJobKinds.Exchange)
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
            job.ErrorMessage = "interrupted on API startup";
            job.CompletedAt = now;
        }

        var incompleteExchangeJobs = incomplete
            .Where(j => j.Kind == CoachJobKinds.Exchange)
            .Select(j => j.Id)
            .ToList();
        if (incompleteExchangeJobs.Count > 0)
        {
            var exchanges = await _db.CoachExchanges
                .Where(e => e.JobId != null && incompleteExchangeJobs.Contains(e.JobId.Value)
                            && (e.Status == CoachExchangeStatuses.Queued
                                || e.Status == CoachExchangeStatuses.Running))
                .ToListAsync(cancellationToken);
            foreach (var exchange in exchanges)
            {
                exchange.Status = CoachExchangeStatuses.Failed;
                exchange.ErrorCode = CoachJobErrorCodes.Interrupted;
                exchange.ErrorMessage = "interrupted on API startup";
                exchange.CompletedAt = now;
                exchange.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private Task<CoachJob?> FindActiveRitualJobAsync(CancellationToken cancellationToken) =>
        _db.CoachJobs.AsNoTracking()
            .Where(j => (j.Status == CoachJobStatuses.Queued || j.Status == CoachJobStatuses.Running)
                        && j.Kind != CoachJobKinds.Exchange)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<CoachJob?> FindActiveExchangeJobAsync(CancellationToken cancellationToken) =>
        _db.CoachJobs.AsNoTracking()
            .Where(j => (j.Status == CoachJobStatuses.Queued || j.Status == CoachJobStatuses.Running)
                        && j.Kind == CoachJobKinds.Exchange)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
