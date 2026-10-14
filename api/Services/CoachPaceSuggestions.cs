namespace Tempo.Api.Services;

/// <summary>
/// Suggests training paces (sec/km) from a best-effort race pace.
/// Heuristic only — athlete must confirm before Layer 1 persist.
/// Prefers 5K, falls back to 1 mile.
/// </summary>
public static class CoachPaceSuggestions
{
    public const string PreferredDistance = "5K";
    public const string FallbackDistance = "1 mile";

    /// <summary>
    /// Derive suggested paces from best-effort time and distance.
    /// easy ≈ race×1.3, tempo ≈ race×1.1, threshold ≈ race, interval ≈ race×0.95.
    /// </summary>
    public static CoachLayer1Paces? SuggestFromBestEffort(string distance, double distanceM, int timeS)
    {
        if (distanceM <= 0 || timeS <= 0)
        {
            return null;
        }

        var raceSecPerKm = timeS / (distanceM / 1000.0);
        if (raceSecPerKm <= 0 || double.IsNaN(raceSecPerKm) || double.IsInfinity(raceSecPerKm))
        {
            return null;
        }

        return new CoachLayer1Paces(
            EasySecPerKm: RoundPace(raceSecPerKm * 1.3),
            TempoSecPerKm: RoundPace(raceSecPerKm * 1.1),
            ThresholdSecPerKm: RoundPace(raceSecPerKm),
            IntervalSecPerKm: RoundPace(raceSecPerKm * 0.95));
    }

    public static CoachLayer1Paces? SuggestFromBestEfforts(
        IEnumerable<(string Distance, double DistanceM, int TimeS)> efforts)
    {
        var list = efforts.ToList();
        var preferred = list.FirstOrDefault(e =>
            string.Equals(e.Distance, PreferredDistance, StringComparison.OrdinalIgnoreCase));
        if (preferred.Distance != null)
        {
            return SuggestFromBestEffort(preferred.Distance, preferred.DistanceM, preferred.TimeS);
        }

        var fallback = list.FirstOrDefault(e =>
            string.Equals(e.Distance, FallbackDistance, StringComparison.OrdinalIgnoreCase));
        if (fallback.Distance != null)
        {
            return SuggestFromBestEffort(fallback.Distance, fallback.DistanceM, fallback.TimeS);
        }

        return null;
    }

    private static int RoundPace(double secPerKm) =>
        Math.Max(1, (int)Math.Round(secPerKm, MidpointRounding.AwayFromZero));
}
