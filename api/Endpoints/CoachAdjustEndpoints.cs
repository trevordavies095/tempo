using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

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
                "Athlete-initiated only. Requires free-text reason; optional category niggle|illness|life|other. " +
                "Does not call Anthropic. Creates a started adjustment; athlete must re-run privacy pre-flight, consent, generate, review, and apply. " +
                "409 if no remaining days or an adjust is already in progress.");

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
            request.Category,
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

        return Results.Ok(new CoachWeekPrivacyReceiptDocument(
            WeekId: week.Id,
            WeekStartDate: week.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Status: week.Status,
            PayloadHash: week.PayloadHash,
            RedactedPayload: payload,
            SelectedEpisodeIds: episodeIds,
            SelectedRuleIds: ruleIds));
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
            UpdatedAt: adjustment.UpdatedAt);

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
    string? Category,
    int? TimezoneOffsetMinutes,
    string? ReferenceDate);

public sealed record CoachAdjustmentDocument(
    Guid Id,
    string WeekStartDate,
    Guid SourceCommittedWeekId,
    string Reason,
    string? Category,
    string LockedThroughDate,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CoachWeekPrivacyReceiptDocument(
    Guid WeekId,
    string WeekStartDate,
    string Status,
    string PayloadHash,
    object? RedactedPayload,
    IReadOnlyList<string> SelectedEpisodeIds,
    IReadOnlyList<string> SelectedRuleIds);
