using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public class CoachJobWorker : BackgroundService
{
    private readonly CoachJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CoachJobWorker> _logger;

    public CoachJobWorker(
        CoachJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<CoachJobWorker> logger)
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
            var jobs = scope.ServiceProvider.GetRequiredService<CoachJobService>();
            await jobs.InterruptIncompleteJobsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark interrupted coach jobs on startup");
        }

        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing coach job {JobId}", jobId);
            }
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var brain = scope.ServiceProvider.GetRequiredService<ICoachProtocolBrain>();
        var protector = scope.ServiceProvider.GetRequiredService<CoachAnthropicSecretProtector>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CoachJobWorker>>();

        var job = await db.CoachJobs.FindAsync([jobId], CancellationToken.None);
        if (job == null)
        {
            logger.LogWarning("Coach job {JobId} was dequeued but not found", jobId);
            return;
        }

        if (job.Status != CoachJobStatuses.Queued)
        {
            return;
        }

        job.Status = CoachJobStatuses.Running;
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        var preflight = await db.CoachPrivacyPreflights
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == job.PreflightId, CancellationToken.None);
        if (preflight == null || preflight.ConsentedAt == null)
        {
            await FailAsync(db, job, CoachJobErrorCodes.Validation, "Consented privacy pre-flight is required");
            return;
        }

        var provider = await db.CoachProviderConnections.AsNoTracking()
            .FirstOrDefaultAsync(CancellationToken.None);
        if (provider == null)
        {
            await FailAsync(db, job, CoachJobErrorCodes.Auth, "Anthropic provider key is missing");
            return;
        }

        string apiKey;
        try
        {
            apiKey = protector.Decrypt(provider.ApiKeyCiphertext);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decrypt Coach Anthropic key for job {JobId}", jobId);
            await FailAsync(db, job, CoachJobErrorCodes.Auth, "Failed to decrypt Anthropic API key");
            return;
        }

        var constraints = await db.CoachConstraints.AsNoTracking()
            .Where(c => !c.IsRetired)
            .ToListAsync(CancellationToken.None);

        var packetJson = preflight.RedactedPayloadJson;
        var modelId = provider.ModelId;
        string? repairHint = null;
        var totalIn = 0;
        var totalOut = 0;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1 && repairHint != null)
            {
                job.SchemaRepairAttempted = true;
                await db.SaveChangesAsync(CancellationToken.None);
            }

            CoachProtocolBrainResult brainResult;
            try
            {
                brainResult = await brain.GenerateWeeklyCommitAsync(
                    packetJson,
                    modelId,
                    apiKey,
                    repairHint,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await FailAsync(db, job, CoachJobErrorCodes.Interrupted, "cancelled");
                return;
            }

            if (brainResult.InputTokens is int tin)
            {
                totalIn += tin;
            }

            if (brainResult.OutputTokens is int tout)
            {
                totalOut += tout;
            }

            job.InputTokens = totalIn;
            job.OutputTokens = totalOut;

            if (!brainResult.Success)
            {
                // One immediate retry on transient/timeout only (not auth/billing).
                if (attempt == 0
                    && brainResult.Error is CoachProtocolBrainError.Timeout or CoachProtocolBrainError.Unknown
                        or CoachProtocolBrainError.RateLimited)
                {
                    repairHint = null;
                    continue;
                }

                await FailAsync(
                    db,
                    job,
                    MapBrainError(brainResult.Error),
                    brainResult.ErrorMessage ?? "Protocol brain failed");
                return;
            }

            var schemaError = CoachWeeklyCommitSchema.TryParseAndValidate(
                brainResult.CommitJson,
                preflight.WeekStartDate,
                out var commit);
            if (schemaError != null)
            {
                if (attempt == 0)
                {
                    repairHint = schemaError;
                    continue;
                }

                await FailAsync(db, job, CoachJobErrorCodes.Validation, schemaError);
                return;
            }

            if (job.Kind == CoachJobKinds.Adjust && job.AdjustmentId is Guid adjustmentId)
            {
                var spliceError = await TrySpliceAdjustAsync(
                    db,
                    adjustmentId,
                    preflight.WeekStartDate,
                    commit!,
                    CancellationToken.None);
                if (spliceError != null)
                {
                    await FailAsync(db, job, CoachJobErrorCodes.Validation, spliceError);
                    return;
                }
            }

            var constraintError = CoachCommitConstraintValidator.Validate(commit!, constraints);
            if (constraintError != null)
            {
                // Never LM-auto-repair constraint failures.
                await FailAsync(db, job, CoachJobErrorCodes.Validation, constraintError);
                return;
            }

            var now = DateTime.UtcNow;
            var week = await db.CoachWeeks
                .FirstOrDefaultAsync(
                    w => w.WeekStartDate == preflight.WeekStartDate && w.Status == CoachWeekStatuses.Draft,
                    CancellationToken.None);
            if (week == null)
            {
                week = new CoachWeek
                {
                    WeekStartDate = preflight.WeekStartDate,
                    Status = CoachWeekStatuses.Draft,
                    CreatedAt = now
                };
                db.CoachWeeks.Add(week);
            }

            week.CommitJson = CoachWeeklyCommitSchema.Serialize(commit!);
            week.PreflightId = preflight.Id;
            week.JobId = job.Id;
            week.PayloadHash = preflight.PayloadHash;
            week.RedactedPayloadJson = preflight.RedactedPayloadJson;
            week.UpdatedAt = now;

            await db.SaveChangesAsync(CancellationToken.None);

            job.Status = CoachJobStatuses.Ready;
            job.ResultWeekId = week.Id;
            job.CompletedAt = now;
            job.ErrorCode = null;
            job.ErrorMessage = null;
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        await FailAsync(db, job, CoachJobErrorCodes.Unknown, "Generate exhausted retries without a result");
    }

    private static string MapBrainError(CoachProtocolBrainError error) => error switch
    {
        CoachProtocolBrainError.RateLimited => CoachJobErrorCodes.RateLimited,
        CoachProtocolBrainError.Auth => CoachJobErrorCodes.Auth,
        CoachProtocolBrainError.Billing => CoachJobErrorCodes.Billing,
        CoachProtocolBrainError.Timeout => CoachJobErrorCodes.Timeout,
        _ => CoachJobErrorCodes.Unknown
    };

    /// <summary>
    /// Overwrites locked days on <paramref name="commit"/> from the source committed plan.
    /// </summary>
    private static async Task<string?> TrySpliceAdjustAsync(
        TempoDbContext db,
        Guid adjustmentId,
        DateOnly weekStartMonday,
        CoachWeeklyCommit commit,
        CancellationToken cancellationToken)
    {
        var adjustment = await db.CoachAdjustments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == adjustmentId, cancellationToken);
        if (adjustment == null || adjustment.Status != CoachAdjustmentStatuses.Started)
        {
            return "Adjust session not found or not active";
        }

        var source = await db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == adjustment.SourceCommittedWeekId, cancellationToken);
        if (source == null)
        {
            return "Source committed week for adjust not found";
        }

        var sourceError = CoachWeeklyCommitSchema.TryParseAndValidate(
            source.CommitJson,
            weekStartMonday,
            out var sourceCommit);
        if (sourceError != null || sourceCommit == null)
        {
            return sourceError ?? "Source committed week commit is invalid";
        }

        var spliced = CoachWeekSplice.SpliceLockedDays(sourceCommit, commit, adjustment.LockedThroughDate);
        var lockError = CoachWeekSplice.ValidateLockedDaysUnchanged(
            sourceCommit,
            spliced,
            adjustment.LockedThroughDate);
        if (lockError != null)
        {
            return lockError;
        }

        // Mutate the parsed commit in place for subsequent serialize.
        commit.CoachesNotes = spliced.CoachesNotes;
        commit.Days = spliced.Days;
        commit.SafetyFlags = spliced.SafetyFlags;
        commit.MemoryProposals = spliced.MemoryProposals;
        commit.ClarifyingQuestion = spliced.ClarifyingQuestion;
        commit.Callouts = spliced.Callouts;
        return null;
    }

    private static async Task FailAsync(TempoDbContext db, CoachJob job, string code, string message)
    {
        job.Status = CoachJobStatuses.Failed;
        job.ErrorCode = code;
        job.ErrorMessage = message.Length > 2000 ? message[..2000] : message;
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
