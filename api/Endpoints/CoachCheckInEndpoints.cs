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
    private const int MaxOverallFeelLength = 500;
    private const int MaxNigglesLength = 2000;
    private const int MaxLifeConstraintsLength = 2000;
    private const int MaxRunNotesLength = 4000;

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
                "Runs autofill from Tempo workouts in the full week window; saved note overlays apply without writing Workout.Notes. " +
                "adherenceHints compare the committed coach plan (when present) to logged Tempo runs. " +
                "404 when Coach is unavailable or the athlete has not opted in.");

        group.MapPut("/check-in", PutCheckIn)
            .WithName("PutCoachCheckIn")
            .Accepts<PutCoachCheckInRequest>("application/json")
            .Produces<CoachCheckInDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Save weekly Coach check-in")
            .WithDescription(
                "Upserts optional subjective fields and per-run note overlays for the week. " +
                "Does not mutate Workout rows. Empty weeks and skipped subjective fields are allowed.");
    }

    private static async Task<IResult> GetCheckIn(
        TempoDbContext db,
        CoachConfig coachConfig,
        [FromQuery] int? timezoneOffsetMinutes = null,
        [FromQuery] string? referenceDate = null)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (!TryResolveWeek(timezoneOffsetMinutes, referenceDate, out var week, out var error))
        {
            return Results.BadRequest(new { error });
        }

        var document = await BuildDocumentAsync(db, week, timezoneOffsetMinutes ?? 0);
        return Results.Ok(document);
    }

    private static async Task<IResult> PutCheckIn(
        [FromBody] PutCoachCheckInRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request == null)
        {
            return Results.BadRequest(new { error = "Request body is required" });
        }

        var offset = request.TimezoneOffsetMinutes;
        DateTime? referenceLocal = null;
        if (!string.IsNullOrWhiteSpace(request.ReferenceDate))
        {
            if (!DateOnly.TryParse(request.ReferenceDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedRef))
            {
                return Results.BadRequest(new { error = "Invalid referenceDate; use yyyy-MM-dd." });
            }

            referenceLocal = parsedRef.ToDateTime(TimeOnly.MinValue);
        }
        else if (!string.IsNullOrWhiteSpace(request.WeekStartDate)
                 && DateOnly.TryParse(request.WeekStartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var weekStart))
        {
            referenceLocal = weekStart.ToDateTime(TimeOnly.MinValue);
        }

        if (!TryResolveWeek(offset, referenceLocal?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var week, out var error))
        {
            return Results.BadRequest(new { error });
        }

        var overallFeel = NormalizeOptional(request.OverallFeel, MaxOverallFeelLength, "overallFeel", out var fieldError);
        if (fieldError != null)
        {
            return Results.BadRequest(new { error = fieldError });
        }

        var niggles = NormalizeOptional(request.Niggles, MaxNigglesLength, "niggles", out fieldError);
        if (fieldError != null)
        {
            return Results.BadRequest(new { error = fieldError });
        }

        var lifeConstraints = NormalizeOptional(request.LifeConstraints, MaxLifeConstraintsLength, "lifeConstraints", out fieldError);
        if (fieldError != null)
        {
            return Results.BadRequest(new { error = fieldError });
        }

        var runNotes = new List<CoachCheckInRunNote>();
        if (request.RunNotes != null)
        {
            foreach (var note in request.RunNotes)
            {
                if (note.WorkoutId == Guid.Empty)
                {
                    return Results.BadRequest(new { error = "runNotes.workoutId is required" });
                }

                var notes = NormalizeOptional(note.Notes, MaxRunNotesLength, "runNotes.notes", out fieldError);
                if (fieldError != null)
                {
                    return Results.BadRequest(new { error = fieldError });
                }

                // Skip empty overlays so GET can fall back to Workout.Notes
                if (string.IsNullOrWhiteSpace(notes))
                {
                    continue;
                }

                runNotes.Add(new CoachCheckInRunNote
                {
                    WorkoutId = note.WorkoutId,
                    Notes = notes
                });
            }
        }

        var weekStartDate = DateOnly.FromDateTime(week.WeekStartLocal);
        var now = DateTime.UtcNow;
        var row = await db.CoachCheckIns.FirstOrDefaultAsync(c => c.WeekStartDate == weekStartDate);
        if (row == null)
        {
            row = new CoachCheckIn
            {
                WeekStartDate = weekStartDate,
                CreatedAt = now
            };
            db.CoachCheckIns.Add(row);
        }

        row.TimezoneOffsetMinutes = offset ?? 0;
        row.OverallFeel = overallFeel;
        row.Niggles = niggles;
        row.LifeConstraints = lifeConstraints;
        row.RunNotesJson = CoachCheckInRunNotesJson.Serialize(runNotes);
        row.UpdatedAt = now;

        await db.SaveChangesAsync();

        // Re-read workouts for response; never touch Workout.Notes
        var document = await BuildDocumentAsync(db, week, row.TimezoneOffsetMinutes);
        return Results.Ok(document);
    }

    private static async Task<CoachCheckInDocument> BuildDocumentAsync(
        TempoDbContext db,
        WeeklyRecapWeekMath.MondaySundayWeek week,
        int timezoneOffsetMinutes)
    {
        var weekStartDate = DateOnly.FromDateTime(week.WeekStartLocal);
        var saved = await db.CoachCheckIns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WeekStartDate == weekStartDate);

        var range = week.FullWeekRange;
        var workouts = await db.Workouts.AsNoTracking()
            .Where(w => w.StartedAt >= range.StartUtc && w.StartedAt <= range.EndUtc)
            .OrderBy(w => w.StartedAt)
            .ToListAsync();

        var noteOverlays = CoachCheckInRunNotesJson.Deserialize(saved?.RunNotesJson)
            .GroupBy(n => n.WorkoutId)
            .ToDictionary(g => g.Key, g => g.Last().Notes);

        var runs = workouts.Select(w =>
        {
            noteOverlays.TryGetValue(w.Id, out var overlay);
            var hasOverlay = noteOverlays.ContainsKey(w.Id);
            return new CoachCheckInRunDocument(
                WorkoutId: w.Id,
                StartedAt: w.StartedAt,
                DistanceM: w.DistanceM,
                DurationS: w.DurationS,
                AvgHeartRateBpm: w.AvgHeartRateBpm,
                RunType: w.RunType,
                Name: w.Name,
                WorkoutNotes: w.Notes,
                Notes: hasOverlay ? overlay : w.Notes,
                NotesEdited: hasOverlay);
        }).ToList();

        var settings = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync();
        var unitPreference = string.IsNullOrWhiteSpace(settings?.UnitPreference)
            ? "metric"
            : settings!.UnitPreference;

        var adherenceHints = await BuildAdherenceHintsAsync(db, weekStartDate, workouts);

        return new CoachCheckInDocument(
            WeekStartDate: weekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WeekEndDate: week.WeekEndLocal.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReferenceDate: week.ReferenceLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimezoneOffsetMinutes: timezoneOffsetMinutes,
            UnitPreference: unitPreference,
            OverallFeel: saved?.OverallFeel,
            Niggles: saved?.Niggles,
            LifeConstraints: saved?.LifeConstraints,
            Runs: runs,
            AdherenceHints: adherenceHints,
            Saved: saved != null,
            UpdatedAt: saved?.UpdatedAt);
    }

    private static async Task<IReadOnlyList<CoachAdherenceHintDocument>> BuildAdherenceHintsAsync(
        TempoDbContext db,
        DateOnly weekStartDate,
        IReadOnlyList<Workout> workouts)
    {
        var committed = await db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(w =>
                w.WeekStartDate == weekStartDate && w.Status == CoachWeekStatuses.Committed);
        if (committed == null)
        {
            return [];
        }

        var schemaError = CoachWeeklyCommitSchema.TryParseAndValidate(
            committed.CommitJson,
            weekStartDate,
            out var commit);
        if (schemaError != null || commit == null)
        {
            return [];
        }

        var planned = commit.Days.Select(d =>
        {
            _ = DateOnly.TryParse(d.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
            double? distance = d.Run?.DistanceM;
            return new CoachAdherenceHints.PlannedDay(date, d.DayType, distance);
        }).ToList();

        // Workouts are already filtered to the week UTC window; map to local calendar date
        // using the stored StartedAt date portion (same convention as check-in run list).
        var actual = workouts
            .Select(w => new CoachAdherenceHints.ActualRun(
                DateOnly.FromDateTime(w.StartedAt),
                w.DistanceM))
            .ToList();

        return CoachAdherenceHints.Build(planned, actual)
            .Select(h => new CoachAdherenceHintDocument(
                h.Code,
                h.Message,
                h.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static bool TryResolveWeek(
        int? timezoneOffsetMinutes,
        string? referenceDate,
        out WeeklyRecapWeekMath.MondaySundayWeek week,
        out string? error)
    {
        week = null!;
        error = null;
        DateTime? referenceLocalDate = null;
        if (!string.IsNullOrWhiteSpace(referenceDate))
        {
            if (!DateTime.TryParse(
                    referenceDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                error = "Invalid referenceDate; use yyyy-MM-dd.";
                return false;
            }

            referenceLocalDate = parsed.Date;
        }

        week = referenceLocalDate.HasValue
            ? WeeklyRecapWeekMath.ResolveMondaySundayWeek(
                DateTime.UtcNow,
                timezoneOffsetMinutes,
                referenceLocalDate)
            : WeeklyRecapWeekMath.ResolveDefaultCheckInWeek(
                DateTime.UtcNow,
                timezoneOffsetMinutes);
        return true;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string fieldName, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            error = $"{fieldName} must be at most {maxLength} characters";
            return null;
        }

        return trimmed;
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

public sealed record CoachCheckInDocument(
    string WeekStartDate,
    string WeekEndDate,
    string ReferenceDate,
    int TimezoneOffsetMinutes,
    string UnitPreference,
    string? OverallFeel,
    string? Niggles,
    string? LifeConstraints,
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
    bool NotesEdited);

public sealed record CoachAdherenceHintDocument(
    string Code,
    string Message,
    string? Date);

public sealed record PutCoachCheckInRequest(
    string? WeekStartDate,
    string? ReferenceDate,
    int? TimezoneOffsetMinutes,
    string? OverallFeel,
    string? Niggles,
    string? LifeConstraints,
    IReadOnlyList<PutCoachCheckInRunNoteRequest>? RunNotes);

public sealed record PutCoachCheckInRunNoteRequest(
    Guid WorkoutId,
    string? Notes);
