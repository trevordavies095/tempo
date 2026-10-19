using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class CoachGenerateEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/generate", PostGenerate)
            .WithName("PostCoachGenerate")
            .Accepts<PostCoachGenerateRequest>("application/json")
            .Produces<CoachJobDocument>(202)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithSummary("Start a coach weekly generate job")
            .WithDescription(
                "Requires a consented privacy pre-flight and Anthropic provider key. " +
                "Enqueues a background job (one active coach job). Replaces an existing draft week; " +
                "409 if the week is already committed or another coach job is active.");

        group.MapGet("/jobs/current", GetCurrentJob)
            .WithName("GetCurrentCoachJob")
            .Produces<CoachJobDocument>(200)
            .Produces(404)
            .WithSummary("Get the active or latest coach generate job");

        group.MapGet("/jobs/{id:guid}", GetJob)
            .WithName("GetCoachJob")
            .Produces<CoachJobDocument>(200)
            .Produces(404)
            .WithSummary("Poll a coach generate job by id");

        group.MapGet("/weeks/draft", GetDraftWeek)
            .WithName("GetCoachDraftWeek")
            .Produces<CoachWeekDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Get the draft weekly commit for a weekStartDate (yyyy-MM-dd)");

        group.MapDelete("/weeks/draft", DeleteDraftWeek)
            .WithName("DeleteCoachDraftWeek")
            .Produces(204)
            .Produces(400)
            .Produces(404)
            .WithSummary("Delete a draft week and cancel a started adjustment")
            .WithDescription(
                "Removes the draft CoachWeek for weekStartDate. Cancels a Started adjustment tied to that week. " +
                "404 when no draft exists.");

        group.MapPost("/weeks/draft/ask-for-changes", PostAskForChangesDraft)
            .WithName("PostCoachDraftAskForChanges")
            .Accepts<PostCoachDraftAskForChangesRequest>("application/json")
            .Produces<CoachJobDocument>(202)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithSummary("Ask for changes on a draft week")
            .WithDescription(
                "Re-assembles the privacy packet with athleteFeedback (note + previous draft summary), " +
                "records consent, and enqueues a generate job that replaces the draft. " +
                "409 when a job is active or the week is committed; 404 when no draft exists.");
    }

    private static async Task<IResult> PostGenerate(
        [FromBody] PostCoachGenerateRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachJobService jobService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request?.PreflightId is null || request.PreflightId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "preflightId is required" });
        }

        var (job, error, status) = await jobService.StartGenerateAsync(request.PreflightId.Value);
        return status switch
        {
            202 when job != null => Results.Json(ToJobDocument(job), statusCode: 202),
            404 => Results.NotFound(new { error }),
            409 => Results.Conflict(new { error }),
            _ => Results.BadRequest(new { error })
        };
    }

    private static async Task<IResult> GetJob(
        Guid id,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachJobService jobService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var job = await jobService.GetAsync(id);
        return job == null ? Results.NotFound() : Results.Ok(ToJobDocument(job));
    }

    private static async Task<IResult> GetCurrentJob(
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachJobService jobService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var job = await jobService.GetCurrentAsync();
        return job == null ? Results.NotFound() : Results.Ok(ToJobDocument(job));
    }

    private static async Task<IResult> GetDraftWeek(
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachAdjustmentService adjustmentService,
        [FromQuery] string? weekStartDate)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (!DateOnly.TryParse(weekStartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var monday))
        {
            return Results.BadRequest(new { error = "weekStartDate must be yyyy-MM-dd" });
        }

        var week = await db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(w => w.WeekStartDate == monday && w.Status == CoachWeekStatuses.Draft);
        if (week == null)
        {
            return Results.NotFound();
        }

        var adjustment = await adjustmentService.FindForWeekDocumentAsync(
            week.WeekStartDate,
            week.JobId,
            week.Status);
        return Results.Ok(CoachWeekDocumentMapper.ToDocument(week, adjustment));
    }

    private static async Task<IResult> DeleteDraftWeek(
        TempoDbContext db,
        CoachConfig coachConfig,
        [FromQuery] string? weekStartDate)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (!DateOnly.TryParse(weekStartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var monday))
        {
            return Results.BadRequest(new { error = "weekStartDate must be yyyy-MM-dd" });
        }

        var drafts = await db.CoachWeeks
            .Where(w => w.WeekStartDate == monday && w.Status == CoachWeekStatuses.Draft)
            .ToListAsync();
        if (drafts.Count == 0)
        {
            return Results.NotFound();
        }

        db.CoachWeeks.RemoveRange(drafts);

        var startedAdjustments = await db.CoachAdjustments
            .Where(a => a.WeekStartDate == monday && a.Status == CoachAdjustmentStatuses.Started)
            .ToListAsync();
        foreach (var adj in startedAdjustments)
        {
            adj.Status = CoachAdjustmentStatuses.Cancelled;
            adj.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> PostAskForChangesDraft(
        [FromBody] PostCoachDraftAskForChangesRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachPrivacyPreflightService preflightService,
        CoachJobService jobService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request == null)
        {
            return Results.BadRequest(new { error = "Request body is required" });
        }

        if (!DateOnly.TryParse(
                request.WeekStartDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var planMonday))
        {
            return Results.BadRequest(new { error = "weekStartDate must be yyyy-MM-dd" });
        }

        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return Results.BadRequest(new { error = "note is required" });
        }

        var committed = await db.CoachWeeks.AsNoTracking()
            .AnyAsync(w => w.WeekStartDate == planMonday && w.Status == CoachWeekStatuses.Committed);
        if (committed)
        {
            return Results.Conflict(new
            {
                error =
                    $"Plan week {planMonday:yyyy-MM-dd} is already committed. Use Ask for changes on the committed plan."
            });
        }

        var draft = await db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(w => w.WeekStartDate == planMonday && w.Status == CoachWeekStatuses.Draft);
        if (draft == null)
        {
            return Results.NotFound(new { error = "No draft week found for weekStartDate" });
        }

        // Capture previous draft before StartGenerateAsync deletes it.
        var previousCommitJson = draft.CommitJson;
        var offset = request.TimezoneOffsetMinutes ?? 0;

        var (preflight, assembleError, assembleStatus) = await preflightService.AssembleDraftAskForChangesAsync(
            planMonday,
            request.Note,
            previousCommitJson,
            offset);
        if (preflight == null)
        {
            return assembleStatus switch
            {
                400 => Results.BadRequest(new { error = assembleError }),
                _ => Results.BadRequest(new { error = assembleError ?? "Failed to assemble privacy packet" })
            };
        }

        var (job, jobError, jobStatus) = await jobService.StartGenerateAsync(preflight.Id);
        return jobStatus switch
        {
            202 when job != null => Results.Json(ToJobDocument(job), statusCode: 202),
            404 => Results.NotFound(new { error = jobError }),
            409 => Results.Conflict(new { error = jobError }),
            _ => Results.BadRequest(new { error = jobError })
        };
    }

    private static CoachJobDocument ToJobDocument(CoachJob job) =>
        new(
            Id: job.Id,
            Status: job.Status,
            Kind: string.IsNullOrWhiteSpace(job.Kind) ? CoachJobKinds.Generate : job.Kind,
            AdjustmentId: job.AdjustmentId,
            WeekStartDate: job.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PreflightId: job.PreflightId,
            ErrorCode: job.ErrorCode,
            ErrorMessage: job.ErrorMessage,
            InputTokens: job.InputTokens,
            OutputTokens: job.OutputTokens,
            SchemaRepairAttempted: job.SchemaRepairAttempted,
            ResultWeekId: job.ResultWeekId,
            CreatedAt: job.CreatedAt,
            StartedAt: job.StartedAt,
            CompletedAt: job.CompletedAt);

    private static async Task<bool> IsCoachAvailableAsync(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return false;
        }

        var prefs = await db.CoachPreferences.AsNoTracking().FirstOrDefaultAsync();
        return prefs?.OptedIn == true;
    }
}

public sealed record PostCoachGenerateRequest(Guid? PreflightId);

public sealed record PostCoachDraftAskForChangesRequest(
    string? WeekStartDate,
    string? Note,
    int? TimezoneOffsetMinutes = null);

public sealed record CoachJobDocument(
    Guid Id,
    string Status,
    string Kind,
    Guid? AdjustmentId,
    string WeekStartDate,
    Guid PreflightId,
    string? ErrorCode,
    string? ErrorMessage,
    int? InputTokens,
    int? OutputTokens,
    bool SchemaRepairAttempted,
    Guid? ResultWeekId,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);

public sealed record CoachWeekDocument(
    Guid Id,
    string WeekStartDate,
    string Status,
    object? Commit,
    Guid PreflightId,
    Guid JobId,
    string PayloadHash,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsAdjustment = false,
    Guid? AdjustmentId = null,
    string? LockedThroughDate = null,
    string? AdjustmentCategory = null,
    string? AdjustmentStatus = null,
    // Apply audit when status is committed; null for drafts / missing record.
    object? ApplyRecord = null,
    Guid? SourceExchangeId = null);
