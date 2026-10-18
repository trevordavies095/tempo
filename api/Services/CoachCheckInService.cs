using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Endpoints;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Shared check-in upsert + document build for PUT and Send.
/// </summary>
public sealed class CoachCheckInService
{
    private const int MaxOverallFeelLength = 500;
    private const int MaxNigglesLength = 2000;
    private const int MaxLifeConstraintsLength = 2000;
    private const int MaxQuestionForCoachLength = 2000;
    private const int MaxRunNotesLength = 4000;

    private readonly TempoDbContext _db;

    public CoachCheckInService(TempoDbContext db)
    {
        _db = db;
    }

    public async Task<(CoachCheckInDocument? Document, string? Error, int StatusCode)> UpsertAsync(
        PutCoachCheckInRequest request,
        CancellationToken cancellationToken = default)
    {
        var offset = request.TimezoneOffsetMinutes;
        DateTime? referenceLocal = null;
        if (!string.IsNullOrWhiteSpace(request.ReferenceDate))
        {
            if (!DateOnly.TryParse(request.ReferenceDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedRef))
            {
                return (null, "Invalid referenceDate; use yyyy-MM-dd.", 400);
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
            return (null, error, 400);
        }

        var overallFeel = NormalizeOptional(request.OverallFeel, MaxOverallFeelLength, "overallFeel", out var fieldError);
        if (fieldError != null)
        {
            return (null, fieldError, 400);
        }

        var niggles = NormalizeOptional(request.Niggles, MaxNigglesLength, "niggles", out fieldError);
        if (fieldError != null)
        {
            return (null, fieldError, 400);
        }

        var lifeConstraints = NormalizeOptional(request.LifeConstraints, MaxLifeConstraintsLength, "lifeConstraints", out fieldError);
        if (fieldError != null)
        {
            return (null, fieldError, 400);
        }

        var questionForCoach = NormalizeOptional(request.QuestionForCoach, MaxQuestionForCoachLength, "questionForCoach", out fieldError);
        if (fieldError != null)
        {
            return (null, fieldError, 400);
        }

        byte? feelScore = null;
        if (request.FeelScore != null)
        {
            if (request.FeelScore is < 1 or > 5)
            {
                return (null, "feelScore must be between 1 and 5", 400);
            }

            feelScore = (byte)request.FeelScore.Value;
        }

        var runNotes = new List<CoachCheckInRunNote>();
        if (request.RunNotes != null)
        {
            foreach (var note in request.RunNotes)
            {
                if (note.WorkoutId == Guid.Empty)
                {
                    return (null, "runNotes.workoutId is required", 400);
                }

                var notes = NormalizeOptional(note.Notes, MaxRunNotesLength, "runNotes.notes", out fieldError);
                if (fieldError != null)
                {
                    return (null, fieldError, 400);
                }

                byte? rpe = null;
                if (note.Rpe != null)
                {
                    if (note.Rpe is < 1 or > 10)
                    {
                        return (null, "runNotes.rpe must be between 1 and 10", 400);
                    }

                    rpe = (byte)note.Rpe.Value;
                }

                // Keep overlays that set notes and/or RPE (RPE-only is valid).
                if (string.IsNullOrWhiteSpace(notes) && rpe == null)
                {
                    continue;
                }

                runNotes.Add(new CoachCheckInRunNote
                {
                    WorkoutId = note.WorkoutId,
                    Notes = notes,
                    Rpe = rpe
                });
            }
        }

        var weekStartDate = DateOnly.FromDateTime(week.WeekStartLocal);
        var now = DateTime.UtcNow;
        var row = await _db.CoachCheckIns.FirstOrDefaultAsync(c => c.WeekStartDate == weekStartDate, cancellationToken);
        if (row == null)
        {
            row = new CoachCheckIn
            {
                WeekStartDate = weekStartDate,
                CreatedAt = now
            };
            _db.CoachCheckIns.Add(row);
        }

        row.TimezoneOffsetMinutes = offset ?? 0;
        row.OverallFeel = overallFeel;
        row.FeelScore = feelScore;
        row.Niggles = niggles;
        row.LifeConstraints = lifeConstraints;
        row.QuestionForCoach = questionForCoach;
        row.RunNotesJson = CoachCheckInRunNotesJson.Serialize(runNotes);
        row.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        var document = await BuildDocumentAsync(week, row.TimezoneOffsetMinutes, cancellationToken);
        return (document, null, 200);
    }

    public async Task<CoachCheckInDocument> BuildDocumentAsync(
        WeeklyRecapWeekMath.MondaySundayWeek week,
        int timezoneOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        var weekStartDate = DateOnly.FromDateTime(week.WeekStartLocal);
        var saved = await _db.CoachCheckIns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WeekStartDate == weekStartDate, cancellationToken);

        var range = week.FullWeekRange;
        var workouts = await _db.Workouts.AsNoTracking()
            .Where(w => w.StartedAt >= range.StartUtc && w.StartedAt <= range.EndUtc)
            .OrderBy(w => w.StartedAt)
            .ToListAsync(cancellationToken);

        var noteOverlays = CoachCheckInRunNotesJson.Deserialize(saved?.RunNotesJson)
            .GroupBy(n => n.WorkoutId)
            .ToDictionary(g => g.Key, g => g.Last());

        var runs = workouts.Select(w =>
        {
            noteOverlays.TryGetValue(w.Id, out var overlay);
            var hasNotesOverlay = overlay != null && overlay.Notes != null;
            var hasRpeOverlay = overlay?.Rpe != null;
            return new CoachCheckInRunDocument(
                WorkoutId: w.Id,
                StartedAt: w.StartedAt,
                DistanceM: w.DistanceM,
                DurationS: w.DurationS,
                AvgHeartRateBpm: w.AvgHeartRateBpm,
                RunType: w.RunType,
                Name: w.Name,
                WorkoutNotes: w.Notes,
                Notes: hasNotesOverlay ? overlay!.Notes : w.Notes,
                NotesEdited: hasNotesOverlay,
                WorkoutRpe: w.Rpe,
                Rpe: hasRpeOverlay ? overlay!.Rpe : w.Rpe,
                RpeEdited: hasRpeOverlay);
        }).ToList();

        var settings = await _db.UserSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var unitPreference = string.IsNullOrWhiteSpace(settings?.UnitPreference)
            ? "metric"
            : settings!.UnitPreference;

        var adherenceHints = await BuildAdherenceHintsAsync(weekStartDate, workouts, cancellationToken);
        var clarifyingQuestion = await GetClarifyingQuestionAsync(weekStartDate, cancellationToken);

        return new CoachCheckInDocument(
            WeekStartDate: weekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WeekEndDate: week.WeekEndLocal.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReferenceDate: week.ReferenceLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimezoneOffsetMinutes: timezoneOffsetMinutes,
            UnitPreference: unitPreference,
            OverallFeel: saved?.OverallFeel,
            FeelScore: saved?.FeelScore,
            Niggles: saved?.Niggles,
            LifeConstraints: saved?.LifeConstraints,
            QuestionForCoach: saved?.QuestionForCoach,
            ClarifyingQuestion: clarifyingQuestion,
            Runs: runs,
            AdherenceHints: adherenceHints,
            Saved: saved != null,
            UpdatedAt: saved?.UpdatedAt);
    }

    private async Task<string?> GetClarifyingQuestionAsync(
        DateOnly checkInWeekStart,
        CancellationToken cancellationToken)
    {
        var committed = await _db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(
                w => w.WeekStartDate == checkInWeekStart && w.Status == CoachWeekStatuses.Committed,
                cancellationToken);
        if (committed == null)
        {
            return null;
        }

        var schemaError = CoachWeeklyCommitSchema.TryParseAndValidate(
            committed.CommitJson,
            checkInWeekStart,
            out var commit);
        if (schemaError != null || commit == null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(commit.ClarifyingQuestion)
            ? null
            : commit.ClarifyingQuestion.Trim();
    }

    private async Task<IReadOnlyList<CoachAdherenceHintDocument>> BuildAdherenceHintsAsync(
        DateOnly weekStartDate,
        IReadOnlyList<Workout> workouts,
        CancellationToken cancellationToken)
    {
        var committed = await _db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(w =>
                w.WeekStartDate == weekStartDate && w.Status == CoachWeekStatuses.Committed, cancellationToken);
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

    public static bool TryResolveWeek(
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
}
