namespace Tempo.Api.Utils;

/// <summary>
/// Utility class for timezone offset conversions.
/// Frontend sends timezoneOffsetMinutes as -getTimezoneOffset() (negative for timezones behind UTC).
/// </summary>
public static class TimezoneHelper
{
    /// <summary>
    /// Converts UTC time to local time using the timezone offset.
    /// </summary>
    /// <param name="utcTime">UTC time to convert</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Local time</returns>
    public static DateTime ToLocalTime(DateTime utcTime, int? timezoneOffsetMinutes)
    {
        if (!timezoneOffsetMinutes.HasValue)
        {
            return utcTime;
        }
        // timezoneOffsetMinutes is already negative (from -getTimezoneOffset()), so add it directly
        return utcTime.AddMinutes(timezoneOffsetMinutes.Value);
    }

    /// <summary>
    /// Converts local time to UTC using the timezone offset.
    /// </summary>
    /// <param name="localTime">Local time to convert</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>UTC time</returns>
    public static DateTime ToUtcTime(DateTime localTime, int? timezoneOffsetMinutes)
    {
        if (!timezoneOffsetMinutes.HasValue)
        {
            return DateTime.SpecifyKind(localTime, DateTimeKind.Utc);
        }
        // To convert local to UTC, we subtract the offset (which is already negative)
        // So we add the negative offset, which effectively subtracts
        return DateTime.SpecifyKind(localTime.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc);
    }

    /// <summary>
    /// Gets the current date/time in local timezone.
    /// </summary>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Current date/time in local timezone</returns>
    public static DateTime GetLocalNow(int? timezoneOffsetMinutes)
    {
        var now = DateTime.UtcNow;
        return ToLocalTime(now, timezoneOffsetMinutes);
    }
}

