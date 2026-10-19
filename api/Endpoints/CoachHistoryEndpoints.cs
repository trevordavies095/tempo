using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class CoachHistoryEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/weeks", ListWeeks)
            .WithName("ListCoachWeeks")
            .Produces<CoachWeeksListDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("List committed and archived coach weeks (newest first, paged)")
            .WithDescription(
                "Returns committed and archived weeks with feelScore, planned/actual distance, and isAdjustment. " +
                "Drafts are excluded. 404 when Coach is disabled or the athlete has not opted in.");

        group.MapGet("/weeks/{weekId:guid}", GetWeekById)
            .WithName("GetCoachWeekById")
            .Produces<CoachWeekHistoryDetailDocument>(200)
            .Produces(404)
            .WithSummary("Get a committed or archived coach week with planned-vs-actual days and privacy receipt")
            .WithDescription(
                "404 for unknown ids and drafts. Includes plan-day matches, apply record, and embedded privacy receipt.");
    }

    private static async Task<IResult> ListWeeks(
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachHistoryService historyService,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? timezoneOffsetMinutes = null)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var (document, error, status) = await historyService.ListAsync(
            page,
            pageSize,
            timezoneOffsetMinutes,
            cancellationToken);

        if (status != 200 || document == null)
        {
            return status switch
            {
                404 => Results.NotFound(new { error }),
                _ => Results.BadRequest(new { error })
            };
        }

        return Results.Ok(document);
    }

    private static async Task<IResult> GetWeekById(
        Guid weekId,
        [FromQuery] int? timezoneOffsetMinutes,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachHistoryService historyService,
        CancellationToken cancellationToken)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var (document, error, status) = await historyService.GetByIdAsync(
            weekId,
            timezoneOffsetMinutes,
            cancellationToken);

        if (status != 200 || document == null)
        {
            return Results.NotFound(new { error });
        }

        return Results.Ok(document);
    }

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

public sealed record CoachWeekListItemDocument(
    Guid Id,
    string WeekStartDate,
    string WeekEndDate,
    string Status,
    bool IsAdjustment,
    byte? FeelScore,
    double PlannedDistanceM,
    double ActualDistanceM);

public sealed record CoachWeeksListDocument(
    IReadOnlyList<CoachWeekListItemDocument> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CoachWeekHistoryDetailDocument(
    Guid Id,
    string WeekStartDate,
    string Status,
    object? Commit,
    Guid PreflightId,
    Guid JobId,
    string PayloadHash,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsAdjustment,
    Guid? AdjustmentId,
    string? LockedThroughDate,
    string? AdjustmentCategory,
    string? AdjustmentStatus,
    object? ApplyRecord,
    IReadOnlyList<CoachPlanDayDocument> Days,
    CoachWeekPrivacyReceiptDocument PrivacyReceipt,
    Guid? SourceExchangeId = null,
    IReadOnlyList<CoachHistoryExchangeDocument>? Exchanges = null);

/// <summary>Read-only mid-week exchange row for History week detail.</summary>
public sealed record CoachHistoryExchangeDocument(
    Guid Id,
    string WeekStartDate,
    Guid? WorkoutId,
    string AthleteText,
    string Status,
    string? Verdict,
    string? CoachSummary,
    Guid PreflightId,
    string? PayloadHash,
    DateTime CreatedAt,
    DateTime? CompletedAt);
