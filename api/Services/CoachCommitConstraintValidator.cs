using System.Globalization;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Packet-aware fail-closed validation of a weekly commit against CoachConstraint non-negotiables.
/// Never LM-auto-repaired.
/// </summary>
public static class CoachCommitConstraintValidator
{
    public static string? Validate(
        CoachWeeklyCommit commit,
        IReadOnlyList<CoachConstraint> activeConstraints)
    {
        if (commit.Days.Count != 7)
        {
            return "days must contain exactly 7 entries";
        }

        var daysByDate = new Dictionary<DateOnly, CoachWeeklyCommitDay>();
        foreach (var day in commit.Days)
        {
            if (!DateOnly.TryParse(day.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return $"Invalid day date: {day.Date}";
            }

            daysByDate[date] = day;
        }

        foreach (var constraint in activeConstraints.Where(c => !c.IsRetired))
        {
            var error = constraint.Kind switch
            {
                CoachConstraintKinds.Blackout => ValidateBlackout(constraint, daysByDate),
                CoachConstraintKinds.StandingSchedule => ValidateStanding(constraint, daysByDate),
                CoachConstraintKinds.SecondaryRace => ValidateSecondaryRace(constraint, daysByDate),
                _ => null
            };

            if (error != null)
            {
                return error;
            }
        }

        return null;
    }

    private static string? ValidateBlackout(
        CoachConstraint constraint,
        IReadOnlyDictionary<DateOnly, CoachWeeklyCommitDay> daysByDate)
    {
        if (constraint.StartDate == null)
        {
            return "blackout constraint missing startDate";
        }

        var start = constraint.StartDate.Value;
        var end = constraint.EndDate ?? start;
        if (end < start)
        {
            (start, end) = (end, start);
        }

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (!daysByDate.TryGetValue(d, out var day))
            {
                continue;
            }

            if (!CoachWeeklyCommitSchema.IsRestDay(day))
            {
                return $"Blackout violated on {d:yyyy-MM-dd}: plan has a non-rest day";
            }
        }

        return null;
    }

    private static readonly string[] IsoWeekdayLabels =
        ["", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private static string? ValidateStanding(
        CoachConstraint constraint,
        IReadOnlyDictionary<DateOnly, CoachWeeklyCommitDay> daysByDate)
    {
        var preset = constraint.Preset;
        var weekdays = constraint.Weekdays ?? [];
        if (string.IsNullOrWhiteSpace(preset) || weekdays.Count == 0)
        {
            return "standing_schedule constraint missing preset or weekdays";
        }

        if (preset == CoachStandingPresets.LongDay)
        {
            return ValidateLongDay(weekdays[0], daysByDate);
        }

        foreach (var isoWeekday in weekdays)
        {
            var day = daysByDate.Values.FirstOrDefault(d =>
                DateOnly.TryParse(d.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && IsoWeekday(date) == isoWeekday);
            if (day == null)
            {
                continue;
            }

            switch (preset)
            {
                case CoachStandingPresets.NoRunOn:
                    if (!CoachWeeklyCommitSchema.IsRestDay(day))
                    {
                        return $"standing_schedule no_run_on violated on {day.Date}";
                    }

                    break;

                case CoachStandingPresets.NoQualityOn:
                    if (IsQuality(day))
                    {
                        return $"standing_schedule no_quality_on violated on {day.Date}";
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// Soft long-day rule: when a long run is scheduled, it must land on the standing weekday.
    /// Weeks with no long run pass (taper / recovery / race week).
    /// </summary>
    private static string? ValidateLongDay(
        int standingIsoWeekday,
        IReadOnlyDictionary<DateOnly, CoachWeeklyCommitDay> daysByDate)
    {
        var label = standingIsoWeekday is >= 1 and <= 7
            ? IsoWeekdayLabels[standingIsoWeekday]
            : standingIsoWeekday.ToString(CultureInfo.InvariantCulture);

        foreach (var (date, day) in daysByDate)
        {
            if (!IsLong(day))
            {
                continue;
            }

            if (IsoWeekday(date) != standingIsoWeekday)
            {
                return $"standing_schedule long_day violated on {date:yyyy-MM-dd}: long runs go on {label}";
            }
        }

        return null;
    }

    private static string? ValidateSecondaryRace(
        CoachConstraint constraint,
        IReadOnlyDictionary<DateOnly, CoachWeeklyCommitDay> daysByDate)
    {
        if (constraint.StartDate == null)
        {
            return "secondary_race constraint missing startDate";
        }

        var raceDate = constraint.StartDate.Value;
        if (!daysByDate.TryGetValue(raceDate, out var day))
        {
            // Race outside this week — not this week's job to enforce.
            return null;
        }

        var runType = day.Run?.Type?.Trim().ToLowerInvariant();
        var dayType = day.DayType?.Trim().ToLowerInvariant();
        if (runType != "race" && dayType != "race")
        {
            return $"secondary_race on {raceDate:yyyy-MM-dd} requires run.type = race";
        }

        return null;
    }

    private static bool IsQuality(CoachWeeklyCommitDay day)
    {
        if (CoachWeeklyCommitSchema.IsRestDay(day))
        {
            return false;
        }

        var t = day.Run?.Type?.Trim().ToLowerInvariant() ?? day.DayType.Trim().ToLowerInvariant();
        return t is "quality" or "race";
    }

    private static bool IsLong(CoachWeeklyCommitDay day)
    {
        if (CoachWeeklyCommitSchema.IsRestDay(day))
        {
            return false;
        }

        var t = day.Run?.Type?.Trim().ToLowerInvariant() ?? day.DayType.Trim().ToLowerInvariant();
        return t == "long";
    }

    /// <summary>ISO weekday Mon=1 … Sun=7.</summary>
    private static int IsoWeekday(DateOnly date)
        => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
}
