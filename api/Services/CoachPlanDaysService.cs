using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Endpoints;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Plan days for a local date range with matched workouts and adherence hints
/// (committed and archived weeks).
/// </summary>
public sealed class CoachPlanDaysService
{
    private const int MaxRangeDaysInclusive = 42;

    private readonly TempoDbContext _db;

    public CoachPlanDaysService(TempoDbContext db)
    {
        _db = db;
    }

    public async Task<(CoachPlanDaysDocument? Document, string? Error, int StatusCode)> GetAsync(
        string? fromRaw,
        string? toRaw,
        int? timezoneOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseDate(fromRaw, out var from, out var fromError))
        {
            return (null, fromError ?? "from is required (yyyy-MM-dd)", 400);
        }

        if (!TryParseDate(toRaw, out var to, out var toError))
        {
            return (null, toError ?? "to is required (yyyy-MM-dd)", 400);
        }

        if (from > to)
        {
            return (null, "from must be on or before to", 400);
        }

        var spanDays = to.DayNumber - from.DayNumber + 1;
        if (spanDays > MaxRangeDaysInclusive)
        {
            return (null, $"Date range cannot exceed {MaxRangeDaysInclusive} days", 400);
        }

        var offset = timezoneOffsetMinutes ?? 0;
        var weekStartMin = from.AddDays(-6);
        var weekStartMax = to;
        var weeks = await _db.CoachWeeks.AsNoTracking()
            .Where(w =>
                (w.Status == CoachWeekStatuses.Committed || w.Status == CoachWeekStatuses.Archived)
                && w.WeekStartDate >= weekStartMin
                && w.WeekStartDate <= weekStartMax)
            .OrderBy(w => w.WeekStartDate)
            .ThenBy(w => w.UpdatedAt)
            .ToListAsync(cancellationToken);

        var plannedDays = new List<(DateOnly Date, string DayType, CoachWeeklyCommitRun? Run)>();
        foreach (var week in weeks)
        {
            var schemaError = CoachWeeklyCommitSchema.TryParseAndValidate(
                week.CommitJson,
                week.WeekStartDate,
                out var commit);
            if (schemaError != null || commit == null)
            {
                continue;
            }

            foreach (var day in commit.Days)
            {
                if (!DateOnly.TryParse(day.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (date < from || date > to)
                {
                    continue;
                }

                plannedDays.Add((date, day.DayType, day.Run));
            }
        }

        // Prefer later weeks (committed over older archived for same date).
        plannedDays = plannedDays
            .GroupBy(d => d.Date)
            .Select(g => g.Last())
            .OrderBy(d => d.Date)
            .ToList();

        var days = await BuildDayDocumentsAsync(from, to, offset, plannedDays, cancellationToken);
        return (new CoachPlanDaysDocument(days), null, 200);
    }

    /// <summary>
    /// Builds plan-day documents for a single committed or archived week (Mon–Sun).
    /// Returns empty when CommitJson fails schema validation.
    /// </summary>
    public async Task<IReadOnlyList<CoachPlanDayDocument>> BuildDaysForWeekAsync(
        CoachWeek week,
        int timezoneOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        var schemaError = CoachWeeklyCommitSchema.TryParseAndValidate(
            week.CommitJson,
            week.WeekStartDate,
            out var commit);
        if (schemaError != null || commit == null)
        {
            return [];
        }

        var plannedDays = new List<(DateOnly Date, string DayType, CoachWeeklyCommitRun? Run)>();
        foreach (var day in commit.Days)
        {
            if (!DateOnly.TryParse(day.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            plannedDays.Add((date, day.DayType, day.Run));
        }

        plannedDays = plannedDays.OrderBy(d => d.Date).ToList();
        if (plannedDays.Count == 0)
        {
            return [];
        }

        var from = plannedDays[0].Date;
        var to = plannedDays[^1].Date;
        return await BuildDayDocumentsAsync(from, to, timezoneOffsetMinutes, plannedDays, cancellationToken);
    }

    /// <summary>
    /// Sums planned run distance from commit JSON and actual workout distance matched by local date.
    /// </summary>
    public async Task<(double PlannedDistanceM, double ActualDistanceM)> SumDistancesForWeekAsync(
        CoachWeek week,
        int timezoneOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        var days = await BuildDaysForWeekAsync(week, timezoneOffsetMinutes, cancellationToken);
        var planned = days.Sum(d => d.Run?.DistanceM ?? 0);
        var actual = days.Sum(d => d.Workouts.Sum(w => w.DistanceM));
        return (planned, actual);
    }

    private async Task<IReadOnlyList<CoachPlanDayDocument>> BuildDayDocumentsAsync(
        DateOnly from,
        DateOnly to,
        int offset,
        IReadOnlyList<(DateOnly Date, string DayType, CoachWeeklyCommitRun? Run)> plannedDays,
        CancellationToken cancellationToken)
    {
        var rangeStartUtc = WeeklyRecapWeekMath.LocalDateRangeStartToUtc(
            from.ToDateTime(TimeOnly.MinValue),
            offset);
        var rangeEndUtc = WeeklyRecapWeekMath.LocalDateRangeEndToUtc(
            to.ToDateTime(TimeOnly.MaxValue),
            offset);

        var workouts = await _db.Workouts.AsNoTracking()
            .Where(w => w.StartedAt >= rangeStartUtc && w.StartedAt <= rangeEndUtc)
            .OrderBy(w => w.StartedAt)
            .Select(w => new
            {
                w.Id,
                w.StartedAt,
                w.DistanceM,
                w.DurationS,
                w.AvgHeartRateBpm
            })
            .ToListAsync(cancellationToken);

        var workoutsByLocalDate = workouts
            .GroupBy(w => LocalDateFromUtc(w.StartedAt, offset))
            .ToDictionary(
                g => g.Key,
                g => g.Select(w => new CoachPlanDayWorkoutDocument(
                    w.Id,
                    w.DistanceM,
                    w.DurationS,
                    w.AvgHeartRateBpm)).ToList());

        var hintPlanned = plannedDays
            .Select(d => new CoachAdherenceHints.PlannedDay(d.Date, d.DayType, d.Run?.DistanceM))
            .ToList();
        var hintActual = workouts
            .Select(w => new CoachAdherenceHints.ActualRun(
                LocalDateFromUtc(w.StartedAt, offset),
                w.DistanceM))
            .ToList();
        var hintsByDate = CoachAdherenceHints.Build(hintPlanned, hintActual)
            .Where(h => h.Date.HasValue)
            .ToDictionary(
                h => h.Date!.Value,
                h => new CoachAdherenceHintDocument(
                    h.Code,
                    h.Message,
                    h.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        var shoeIds = plannedDays
            .Select(d => d.Run?.ShoeId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var shoeNames = shoeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Shoes.AsNoTracking()
                .Where(s => shoeIds.Contains(s.Id))
                .ToDictionaryAsync(
                    s => s.Id,
                    s => $"{s.Brand} {s.Model}".Trim(),
                    cancellationToken);

        return plannedDays.Select(d =>
        {
            workoutsByLocalDate.TryGetValue(d.Date, out var dayWorkouts);
            hintsByDate.TryGetValue(d.Date, out var hint);

            CoachPlanDayRunDocument? runDoc = null;
            if (d.Run != null)
            {
                string? shoeName = null;
                if (d.Run.ShoeId is { } shoeId)
                {
                    shoeNames.TryGetValue(shoeId, out shoeName);
                }

                runDoc = new CoachPlanDayRunDocument(
                    d.Run.Type,
                    d.Run.DistanceM,
                    d.Run.Structure,
                    d.Run.PaceOrHr,
                    d.Run.ShoeId,
                    shoeName,
                    d.Run.Notes);
            }

            return new CoachPlanDayDocument(
                d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                d.DayType,
                runDoc,
                dayWorkouts ?? [],
                hint);
        }).ToList();
    }

    private static DateOnly LocalDateFromUtc(DateTime startedAtUtc, int timezoneOffsetMinutes)
    {
        var local = startedAtUtc.AddMinutes(timezoneOffsetMinutes);
        return DateOnly.FromDateTime(local);
    }

    private static bool TryParseDate(string? raw, out DateOnly date, out string? error)
    {
        date = default;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "from and to are required (yyyy-MM-dd)";
            return false;
        }

        if (!DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            error = "from and to must be yyyy-MM-dd";
            return false;
        }

        return true;
    }
}
