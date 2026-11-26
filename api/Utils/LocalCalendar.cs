namespace Tempo.Api.Utils;

/// <summary>
/// An inclusive UTC window [StartUtc, EndUtc] for querying <c>Workout.StartedAt</c>.
/// </summary>
public sealed record UtcRange(DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// Calendar days and weeks in the client's local frame, for turning local dates into UTC query windows and
/// UTC timestamps back into local days.
/// <para>
/// <see cref="OffsetMinutes"/> is the client's <c>timezoneOffsetMinutes</c>, sent as <c>-getTimezoneOffset()</c>
/// (negative behind UTC, e.g. EST = -300). Null, and <c>default(LocalCalendar)</c>, mean the UTC frame, so
/// callers that send no offset keep UTC day boundaries. A fixed offset does not model DST; clients send the
/// offset in effect at request time.
/// </para>
/// Weeks run Monday through Sunday. Local dates are <see cref="DateTime"/> values whose time part is ignored.
/// </summary>
public readonly record struct LocalCalendar(int? OffsetMinutes)
{
    /// <summary>The UTC frame (no offset).</summary>
    public static LocalCalendar Utc => default;

    public DateTime ToLocal(DateTime utc) =>
        OffsetMinutes.HasValue ? utc.AddMinutes(OffsetMinutes.Value) : utc;

    public DateTime ToUtc(DateTime local) =>
        DateTime.SpecifyKind(OffsetMinutes.HasValue ? local.AddMinutes(-OffsetMinutes.Value) : local, DateTimeKind.Utc);

    /// <summary>The local calendar date at <paramref name="utcNow"/>.</summary>
    public DateTime Today(DateTime utcNow) => LocalDate(utcNow);

    /// <summary>The local calendar date a UTC timestamp falls on.</summary>
    public DateTime LocalDate(DateTime utc) => ToLocal(utc).Date;

    /// <summary>UTC instant of local midnight starting <paramref name="localDate"/>.</summary>
    public DateTime StartOfDayUtc(DateTime localDate) => ToUtc(localDate.Date);

    /// <summary>UTC instant of the last tick of <paramref name="localDate"/> (inclusive end).</summary>
    public DateTime EndOfDayUtc(DateTime localDate) => ToUtc(localDate.Date.AddDays(1).AddTicks(-1));

    /// <summary>UTC window covering whole local days <paramref name="firstLocalDate"/> through <paramref name="lastLocalDate"/>.</summary>
    public UtcRange Days(DateTime firstLocalDate, DateTime lastLocalDate) =>
        new(StartOfDayUtc(firstLocalDate), EndOfDayUtc(lastLocalDate));

    /// <summary>UTC window covering the Monday–Sunday week that starts on <paramref name="weekStartLocal"/>.</summary>
    public UtcRange Week(DateTime weekStartLocal) => Days(weekStartLocal, weekStartLocal.Date.AddDays(6));

    /// <summary>0 for Monday through 6 for Sunday, on the local day a UTC timestamp falls on.</summary>
    public int MondayIndex(DateTime utc) => MondayIndexOfDate(ToLocal(utc));

    /// <summary>Monday of the week containing <paramref name="localDate"/>.</summary>
    public static DateTime WeekStart(DateTime localDate) => localDate.Date.AddDays(-MondayIndexOfDate(localDate));

    private static int MondayIndexOfDate(DateTime date) => ((int)date.DayOfWeek + 6) % 7;
}
