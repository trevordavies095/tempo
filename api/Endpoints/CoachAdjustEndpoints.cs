using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Utils;

namespace Tempo.Api.Endpoints;

public static class CoachAdjustEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/weeks/committed/adjust", PostStartAdjust)
            .WithName("PostCoachWeekAdjust")
            .Accepts<PostCoachAdjustRequest>("application/json")
            .Produces<CoachAdjustmentDocument>(200)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithSummary("Start a mid-week Adjust session")
            .WithDescription(
                "Athlete-initiated only. Requires free-text reason (category removed). " +
                "Does not call Anthropic. Creates a started adjustment; athlete must re-run privacy pre-flight, consent, generate, review, and apply. " +
                "Prefer POST /coach/weeks/committed/ask-for-changes for the one-shot athlete path. " +
                "409 if no remaining days or an adjust is already in progress.");

        group.MapPost("/weeks/committed/ask-for-changes", PostAskForChangesCommitted)
            .WithName("PostCoachCommittedAskForChanges")
            .Accepts<PostCoachCommittedAskForChangesRequest>("application/json")
            .Produces<CoachJobDocument>(202)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithSummary("Ask for changes on a committed week")
            .WithDescription(
                "Creates an Adjustment (reason = note, no category), locks elapsed days, " +
                "assembles the privacy packet with the adjustment block, records consent, and enqueues. " +
                "Optional sourceExchangeId stamps Ask-for-this-change provenance (ready exchange with suggestedChange). " +
                "409 when a job is active or an adjustment is already started; 404 when no committed plan.");

        group.MapGet("/weeks/{weekId:guid}/privacy-receipt", GetPrivacyReceipt)
            .WithName("GetCoachWeekPrivacyReceipt")
            .Produces<CoachWeekPrivacyReceiptDocument>(200)
            .Produces(404)
            .WithSummary("Get the post-send privacy receipt for a coach week")
            .WithDescription(
                "Returns the redacted payload that was sent for the week (committed or archived).");

        group.MapGet("/adjustments/current", GetCurrentAdjustment)
            .WithName("GetCurrentCoachAdjustment")
            .Produces<CoachAdjustmentDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Get the started adjust session for a weekStartDate");
    }

    private static async Task<IResult> PostStartAdjust(
        [FromBody] PostCoachAdjustRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachAdjustmentService adjustmentService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request == null
            || !DateOnly.TryParse(
                request.WeekStartDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var monday))
        {
            return Results.BadRequest(new { error = "weekStartDate is required (yyyy-MM-dd)" });
        }

        DateOnly? referenceDate = null;
        if (!string.IsNullOrWhiteSpace(request.ReferenceDate))
        {
            if (!DateOnly.TryParse(
                    request.ReferenceDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var rd))
            {
                return Results.BadRequest(new { error = "referenceDate must be yyyy-MM-dd" });
            }

            referenceDate = rd;
        }

        var (adjustment, error, status) = await adjustmentService.StartAsync(
            monday,
            request.Reason ?? string.Empty,
            category: null,
            request.TimezoneOffsetMinutes ?? 0,
            referenceDate);

        return status switch
        {
            200 when adjustment != null => Results.Ok(ToDocument(adjustment)),
            404 => Results.NotFound(new { error }),
            409 => Results.Conflict(new { error }),
            _ => Results.BadRequest(new { error })
        };
    }

    private static async Task<IResult> PostAskForChangesCommitted(
        [FromBody] PostCoachCommittedAskForChangesRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachAdjustmentService adjustmentService,
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

        DateOnly? referenceDate = null;
        if (!string.IsNullOrWhiteSpace(request.ReferenceDate))
        {
            if (!DateOnly.TryParse(
                    request.ReferenceDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var rd))
            {
                return Results.BadRequest(new { error = "referenceDate must be yyyy-MM-dd" });
            }

            referenceDate = rd;
        }

        var offset = request.TimezoneOffsetMinutes ?? 0;

        // Check before StartAsync so we do not leave an orphaned Started adjustment.
        var activeJob = await jobService.GetActiveJobAsync();
        if (activeJob != null)
        {
            return Results.Conflict(new { error = "A coach generate job is already active" });
        }

        Guid? sourceExchangeId = null;
        if (!string.IsNullOrWhiteSpace(request.SourceExchangeId))
        {
            if (!Guid.TryParse(request.SourceExchangeId, out var parsedExchangeId))
            {
                return Results.BadRequest(new { error = "sourceExchangeId must be a UUID" });
            }

            sourceExchangeId = parsedExchangeId;
        }

        var (adjustment, startError, startStatus) = await adjustmentService.StartAsync(
            planMonday,
            request.Note,
            category: null,
            offset,
            referenceDate,
            sourceExchangeId);
        if (adjustment == null)
        {
            return startStatus switch
            {
                404 => Results.NotFound(new { error = startError }),
                409 => Results.Conflict(new { error = startError }),
                _ => Results.BadRequest(new { error = startError })
            };
        }

        // Plan week is the requested weekStartDate (not the calendar week of referenceDate),
        // so Sunday ask-for-changes on next week's committed plan still assembles plan N+1.
        var planWeek = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
            DateTime.UtcNow,
            offset,
            planMonday.ToDateTime(TimeOnly.MinValue));
        var checkInWeek = WeeklyRecapWeekMath.ResolveCheckInWeekFromPlan(planWeek, offset);
        var referenceLocal = referenceDate ?? DateOnly.FromDateTime(planWeek.ReferenceLocalDate);

        var (preflight, assembleError, assembleStatus) = await preflightService.AssembleAndPersistAsync(
            checkInWeek,
            planWeek,
            offset,
            referenceLocal,
            recordConsent: true);
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

    private static async Task<IResult> GetCurrentAdjustment(
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

        var adjustment = await adjustmentService.GetStartedAsNoTrackingAsync(monday);
        return adjustment == null ? Results.NotFound() : Results.Ok(ToDocument(adjustment));
    }

    private static async Task<IResult> GetPrivacyReceipt(
        Guid weekId,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var week = await db.CoachWeeks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == weekId);
        if (week == null)
        {
            return Results.NotFound();
        }

        object? payload;
        try
        {
            payload = JsonSerializer.Deserialize<object>(week.RedactedPayloadJson, JsonOptions);
        }
        catch
        {
            payload = null;
        }

        var (episodeIds, ruleIds) = CoachPrivacyRedactor.ExtractSelectedIds(week.RedactedPayloadJson);
        var recentTraining = CoachPrivacyRedactor.ExtractRecentTrainingSummary(week.RedactedPayloadJson);
        var (exchangeCount, exchangeIds) =
            CoachPrivacyRedactor.ExtractCheckInWeekExchangesSummary(week.RedactedPayloadJson);

        return Results.Ok(new CoachWeekPrivacyReceiptDocument(
            WeekId: week.Id,
            WeekStartDate: week.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Status: week.Status,
            PayloadHash: week.PayloadHash,
            RedactedPayload: payload,
            SelectedEpisodeIds: episodeIds,
            SelectedRuleIds: ruleIds,
            RecentTraining: recentTraining,
            ExchangeCount: exchangeCount,
            ExchangeIds: exchangeIds));
    }

    private static CoachAdjustmentDocument ToDocument(CoachAdjustment adjustment) =>
        new(
            Id: adjustment.Id,
            WeekStartDate: adjustment.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SourceCommittedWeekId: adjustment.SourceCommittedWeekId,
            Reason: adjustment.Reason,
            Category: adjustment.Category,
            LockedThroughDate: adjustment.LockedThroughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Status: adjustment.Status,
            CreatedAt: adjustment.CreatedAt,
            UpdatedAt: adjustment.UpdatedAt,
            SourceExchangeId: adjustment.SourceExchangeId);

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

public sealed record PostCoachAdjustRequest(
    string? WeekStartDate,
    string? Reason,
    int? TimezoneOffsetMinutes,
    string? ReferenceDate);

public sealed record PostCoachCommittedAskForChangesRequest(
    string? WeekStartDate,
    string? Note,
    int? TimezoneOffsetMinutes = null,
    string? ReferenceDate = null,
    string? SourceExchangeId = null);

public sealed record CoachAdjustmentDocument(
    Guid Id,
    string WeekStartDate,
    Guid SourceCommittedWeekId,
    string Reason,
    string? Category,
    string LockedThroughDate,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? SourceExchangeId = null);

public sealed record CoachWeekPrivacyReceiptDocument(
    Guid WeekId,
    string WeekStartDate,
    string Status,
    string PayloadHash,
    object? RedactedPayload,
    IReadOnlyList<string> SelectedEpisodeIds,
    IReadOnlyList<string> SelectedRuleIds,
    IReadOnlyList<CoachRecentTrainingWeekReceipt> RecentTraining,
    int ExchangeCount = 0,
    IReadOnlyList<string>? ExchangeIds = null);
