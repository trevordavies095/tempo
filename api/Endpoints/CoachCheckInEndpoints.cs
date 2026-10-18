using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Utils;

namespace Tempo.Api.Endpoints;

public static class CoachCheckInEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/check-in", GetCheckIn)
            .WithName("GetCoachCheckIn")
            .Produces<CoachCheckInDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Get weekly Coach check-in with Tempo autofill")
            .WithDescription(
                "Returns the Mon–Sun check-in for timezoneOffsetMinutes + optional referenceDate (yyyy-MM-dd). " +
                "When referenceDate is omitted: Sat–Sun and Tue–Fri default to the current Mon–Sun; Monday defaults to the last completed Mon–Sun. " +
                "Runs autofill from Tempo workouts; saved note/RPE overlays apply without writing Workout rows. " +
                "Exposes clarifyingQuestion from a committed plan for the check-in week when present. " +
                "404 when Coach is unavailable or the athlete has not opted in.");

        group.MapPut("/check-in", PutCheckIn)
            .WithName("PutCoachCheckIn")
            .Accepts<PutCoachCheckInRequest>("application/json")
            .Produces<CoachCheckInDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Save weekly Coach check-in")
            .WithDescription(
                "Upserts optional subjective fields (feelScore, questionForCoach, notes, RPE overlays) for the week. " +
                "Does not mutate Workout rows. Empty weeks and skipped subjective fields are allowed.");

        group.MapPost("/check-in/send", PostSend)
            .WithName("PostCoachCheckInSend")
            .Accepts<PutCoachCheckInRequest>("application/json")
            .Produces<CoachJobDocument>(202)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithSummary("Save check-in, consent, and enqueue generate")
            .WithDescription(
                "Persists the check-in (same body as PUT), assembles and redacts the packet (fail-closed), " +
                "records consent, and enqueues a generate job for plan week N+1. Returns the job (202). " +
                "400 when no provider key or redaction fails; 409 when another coach job is active or the plan week is already committed.");
    }

    private static async Task<IResult> GetCheckIn(
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachCheckInService checkInService,
        [FromQuery] int? timezoneOffsetMinutes = null,
        [FromQuery] string? referenceDate = null)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (!CoachCheckInService.TryResolveWeek(timezoneOffsetMinutes, referenceDate, out var week, out var error))
        {
            return Results.BadRequest(new { error });
        }

        var document = await checkInService.BuildDocumentAsync(week, timezoneOffsetMinutes ?? 0);
        return Results.Ok(document);
    }

    private static async Task<IResult> PutCheckIn(
        [FromBody] PutCoachCheckInRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachCheckInService checkInService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request == null)
        {
            return Results.BadRequest(new { error = "Request body is required" });
        }

        var (document, error, status) = await checkInService.UpsertAsync(request);
        return status switch
        {
            200 when document != null => Results.Ok(document),
            _ => Results.BadRequest(new { error })
        };
    }

    private static async Task<IResult> PostSend(
        [FromBody] PutCoachCheckInRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachCheckInService checkInService,
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

        var (document, upsertError, upsertStatus) = await checkInService.UpsertAsync(request);
        if (document == null || upsertStatus != 200)
        {
            return Results.BadRequest(new { error = upsertError ?? "Failed to save check-in" });
        }

        if (!CoachCheckInService.TryResolveWeek(
                request.TimezoneOffsetMinutes,
                request.ReferenceDate ?? document.ReferenceDate,
                out var checkInWeek,
                out var weekError))
        {
            return Results.BadRequest(new { error = weekError });
        }

        var offset = request.TimezoneOffsetMinutes ?? document.TimezoneOffsetMinutes;
        var planMonday = DateOnly.FromDateTime(
            WeeklyRecapWeekMath.ResolvePlanWeekFromCheckIn(checkInWeek, offset).WeekStartLocal);

        var committed = await db.CoachWeeks.AsNoTracking()
            .AnyAsync(w => w.WeekStartDate == planMonday && w.Status == CoachWeekStatuses.Committed);
        if (committed)
        {
            return Results.Conflict(new
            {
                error =
                    $"Plan week {planMonday:yyyy-MM-dd} is already committed. Ask for changes instead of sending a new plan."
            });
        }

        var (preflight, assembleError, assembleStatus) = await preflightService.AssembleRitualAsync(
            checkInWeek,
            offset,
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

public sealed record CoachCheckInDocument(
    string WeekStartDate,
    string WeekEndDate,
    string ReferenceDate,
    int TimezoneOffsetMinutes,
    string UnitPreference,
    string? OverallFeel,
    byte? FeelScore,
    string? Niggles,
    string? LifeConstraints,
    string? QuestionForCoach,
    string? ClarifyingQuestion,
    IReadOnlyList<CoachCheckInRunDocument> Runs,
    IReadOnlyList<CoachAdherenceHintDocument> AdherenceHints,
    bool Saved,
    DateTime? UpdatedAt);

public sealed record CoachCheckInRunDocument(
    Guid WorkoutId,
    DateTime StartedAt,
    double DistanceM,
    int DurationS,
    byte? AvgHeartRateBpm,
    string? RunType,
    string? Name,
    string? WorkoutNotes,
    string? Notes,
    bool NotesEdited,
    byte? WorkoutRpe,
    byte? Rpe,
    bool RpeEdited);

public sealed record CoachAdherenceHintDocument(
    string Code,
    string Message,
    string? Date);

public sealed record PutCoachCheckInRequest(
    string? WeekStartDate,
    string? ReferenceDate,
    int? TimezoneOffsetMinutes,
    string? OverallFeel,
    int? FeelScore,
    string? Niggles,
    string? LifeConstraints,
    string? QuestionForCoach,
    IReadOnlyList<PutCoachCheckInRunNoteRequest>? RunNotes);

public sealed record PutCoachCheckInRunNoteRequest(
    Guid WorkoutId,
    string? Notes,
    int? Rpe);
