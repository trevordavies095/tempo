namespace Tempo.Api.Utils;

/// <summary>
/// Monday–Sunday week boundaries shared by weekly-recap stats and Coach check-in.
/// Frontend sends timezoneOffsetMinutes as -getTimezoneOffset().
/// </summary>
public static class WeeklyRecapWeekMath
{
    public readonly record struct UtcRange(DateTime StartUtc, DateTime EndUtc);

    /// <summary>
    /// Full Monday 00:00 through Sunday 23:59:59.9999999 in the local frame, with UTC query range.
    /// </summary>
    public sealed record MondaySundayWeek(
        DateTime ReferenceLocalDate,
        DateTime WeekStartLocal,
        DateTime WeekEndLocal,
        UtcRange FullWeekRange);

    /// <summary>
    /// Weekly-recap context: current window is week-to-date capped at UTC now, plus prior trailing weeks.
    /// </summary>
    public sealed record WeeklyRecapUtcContext(
        DateTime ReferenceLocalDate,
        DateTime WeekStartLocal,
        DateTime WeekEndLocal,
        UtcRange CurrentRange,
        UtcRange PreviousWeekRange,
        UtcRange TrailingWeek2,
        UtcRange TrailingWeek3,
        bool CurrentWeekIsPartial);

    /// <summary>
    /// Resolves the Monday–Sunday week containing <paramref name="referenceLocalDate"/> (or local today).
    /// </summary>
    public static MondaySundayWeek ResolveMondaySundayWeek(
        DateTime utcNow,
        int? timezoneOffsetMinutes,
        DateTime? referenceLocalDate)
    {
        var localAnchorDate = ResolveLocalAnchorDate(utcNow, timezoneOffsetMinutes, referenceLocalDate);
        var daysSinceMonday = ((int)localAnchorDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStartLocal = localAnchorDate.AddDays(-daysSinceMonday);
        var weekEndLocal = weekStartLocal.AddDays(7).AddTicks(-1);
        var fullWeekRange = new UtcRange(
            LocalDateRangeStartToUtc(weekStartLocal, timezoneOffsetMinutes),
            LocalDateRangeEndToUtc(weekEndLocal, timezoneOffsetMinutes));

        return new MondaySundayWeek(localAnchorDate, weekStartLocal, weekEndLocal, fullWeekRange);
    }

    /// <summary>
    /// Builds Monday–Sunday week boundaries and UTC query ranges for weekly-recap.
    /// Current range is week-to-date capped at UTC now.
    /// </summary>
    public static WeeklyRecapUtcContext BuildWeeklyRecapUtcContext(
        DateTime utcNow,
        int? timezoneOffsetMinutes,
        DateTime? referenceLocalDate)
    {
        var week = ResolveMondaySundayWeek(utcNow, timezoneOffsetMinutes, referenceLocalDate);
        var weekStartUtc = week.FullWeekRange.StartUtc;
        var weekEndUtc = week.FullWeekRange.EndUtc;

        var currentEndUtc = utcNow < weekEndUtc ? utcNow : weekEndUtc;
        UtcRange currentRange;
        if (currentEndUtc < weekStartUtc)
        {
            currentRange = new UtcRange(weekStartUtc, weekStartUtc.AddTicks(-1));
        }
        else
        {
            currentRange = new UtcRange(weekStartUtc, currentEndUtc);
        }

        var previousWeekStartLocal = week.WeekStartLocal.AddDays(-7);
        var previousWeekEndLocal = previousWeekStartLocal.AddDays(7).AddTicks(-1);
        var previousWeekRange = new UtcRange(
            LocalDateRangeStartToUtc(previousWeekStartLocal, timezoneOffsetMinutes),
            LocalDateRangeEndToUtc(previousWeekEndLocal, timezoneOffsetMinutes));

        UtcRange TrailingWeek(int offsetFromCurrentWeekStart)
        {
            var start = week.WeekStartLocal.AddDays(-7 * offsetFromCurrentWeekStart);
            var end = start.AddDays(7).AddTicks(-1);
            return new UtcRange(
                LocalDateRangeStartToUtc(start, timezoneOffsetMinutes),
                LocalDateRangeEndToUtc(end, timezoneOffsetMinutes));
        }

        var currentWeekIsPartial = utcNow < weekEndUtc && utcNow >= weekStartUtc;

        return new WeeklyRecapUtcContext(
            week.ReferenceLocalDate,
            week.WeekStartLocal,
            week.WeekEndLocal,
            currentRange,
            previousWeekRange,
            TrailingWeek(2),
            TrailingWeek(3),
            currentWeekIsPartial);
    }

    public static DateTime LocalDateRangeStartToUtc(DateTime localDateMidnight, int? timezoneOffsetMinutes)
    {
        var d = localDateMidnight.Date;
        return timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(d.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }

    public static DateTime LocalDateRangeEndToUtc(DateTime localEndInclusive, int? timezoneOffsetMinutes)
    {
        return timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(localEndInclusive.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(localEndInclusive, DateTimeKind.Utc);
    }

    private static DateTime ResolveLocalAnchorDate(
        DateTime utcNow,
        int? timezoneOffsetMinutes,
        DateTime? referenceLocalDate)
    {
        if (referenceLocalDate.HasValue)
        {
            return referenceLocalDate.Value.Date;
        }

        var localNow = timezoneOffsetMinutes.HasValue
            ? utcNow.AddMinutes(timezoneOffsetMinutes.Value)
            : utcNow;
        return localNow.Date;
    }
}
