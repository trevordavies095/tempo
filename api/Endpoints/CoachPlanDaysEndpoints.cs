using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class CoachPlanDaysEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/plan-days", GetPlanDays)
            .WithName("GetCoachPlanDays")
            .Produces<CoachPlanDaysDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Get committed plan days with matched workouts for a local date range")
            .WithDescription(
                "Returns committed plan days in [from, to] (local calendar dates) with matched workouts " +
                "(by local date using timezoneOffsetMinutes) and adherence hints. " +
                "Empty list when no committed plan covers the range. " +
                "404 when Coach is disabled or the athlete has not opted in.");
    }

    private static async Task<IResult> GetPlanDays(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int? timezoneOffsetMinutes,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachPlanDaysService planDaysService,
        CancellationToken cancellationToken)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var (document, error, status) = await planDaysService.GetAsync(
            from,
            to,
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

public sealed record CoachPlanDaysDocument(IReadOnlyList<CoachPlanDayDocument> Days);

public sealed record CoachPlanDayDocument(
    string Date,
    string DayType,
    CoachPlanDayRunDocument? Run,
    IReadOnlyList<CoachPlanDayWorkoutDocument> Workouts,
    CoachAdherenceHintDocument? AdherenceHint);

public sealed record CoachPlanDayRunDocument(
    string Type,
    double DistanceM,
    string? Structure,
    string? PaceOrHr,
    Guid? ShoeId,
    string? ShoeName,
    string? Notes);

public sealed record CoachPlanDayWorkoutDocument(
    Guid Id,
    double DistanceM,
    int DurationS,
    byte? AvgHeartRateBpm);
