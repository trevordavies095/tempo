using System.Globalization;

namespace Tempo.Api.Services;

/// <summary>
/// Formats canonical seconds-per-km paces into athlete-facing strings for the coach packet.
/// Matches frontend <c>formatPace</c> (imperial: sec/km × 1.609344 → /mi).
/// </summary>
public static class CoachPaceFormat
{
    public const double MetersPerMileFactor = 1.609344;

    /// <summary>
    /// Format a pace stored as seconds per kilometer for the athlete's unit preference.
    /// Returns null when <paramref name="secPerKm"/> is not a positive finite value.
    /// </summary>
    public static string? FormatSecPerKm(double secPerKm, string unitPreference)
    {
        if (secPerKm <= 0 || double.IsNaN(secPerKm) || double.IsInfinity(secPerKm))
        {
            return null;
        }

        var imperial = IsImperial(unitPreference);
        var totalSeconds = imperial
            ? (int)Math.Round(secPerKm * MetersPerMileFactor, MidpointRounding.AwayFromZero)
            : (int)Math.Round(secPerKm, MidpointRounding.AwayFromZero);

        if (totalSeconds <= 0)
        {
            return null;
        }

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        var suffix = imperial ? "/mi" : "/km";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{minutes}:{seconds:D2} {suffix}");
    }

    /// <summary>
    /// Derive average pace from distance and duration, then format for display.
    /// </summary>
    public static string? FormatFromDistanceAndDuration(
        double distanceM,
        int durationS,
        string unitPreference)
    {
        if (distanceM <= 0 || durationS <= 0)
        {
            return null;
        }

        var secPerKm = durationS / (distanceM / 1000.0);
        return FormatSecPerKm(secPerKm, unitPreference);
    }

    /// <summary>
    /// Build athlete-facing pace strings from Layer 1 canonical paces.
    /// </summary>
    public static CoachPacketPacesDisplay FromLayer1Paces(
        CoachLayer1Paces paces,
        string unitPreference)
    {
        return new CoachPacketPacesDisplay(
            Unit: IsImperial(unitPreference) ? "/mi" : "/km",
            Easy: FormatSecPerKm(paces.EasySecPerKm, unitPreference),
            Tempo: FormatSecPerKm(paces.TempoSecPerKm, unitPreference),
            Threshold: FormatSecPerKm(paces.ThresholdSecPerKm, unitPreference),
            Interval: FormatSecPerKm(paces.IntervalSecPerKm, unitPreference));
    }

    public static bool IsImperial(string unitPreference) =>
        unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase);
}
