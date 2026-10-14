namespace Tempo.Api.Services;

/// <summary>
/// Suggests training paces (sec/km) from recent Tempo races.
/// Heuristic only — athlete must confirm before Layer 1 persist.
/// Prefers Race workouts in the last 120 days (Riegel-normalized to 5K-equivalent,
/// then averaged). Falls back to best-effort 5K / 1 mile when no recent races exist.
/// </summary>
public static class CoachPaceSuggestions
{
    public const int RecentRaceWindowDays = 120;
    public const double RiegelExponent = 1.06;
    public const double ReferenceDistanceM = 5000;
    public const double MinRaceDistanceM = 1000;

    public const string PreferredDistance = "5K";
    public const string FallbackDistance = "1 mile";

    /// <summary>
    /// Derive suggested paces from a race pace (sec/km).
    /// easy ≈ race×1.3, tempo ≈ race×1.1, threshold ≈ race, interval ≈ race×0.95.
    /// </summary>
    public static CoachLayer1Paces? SuggestFromRacePace(double raceSecPerKm)
    {
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

    public static CoachLayer1Paces? SuggestFromBestEffort(string distance, double distanceM, int timeS)
    {
        if (distanceM <= 0 || timeS <= 0)
        {
            return null;
        }

        var raceSecPerKm = timeS / (distanceM / 1000.0);
        return SuggestFromRacePace(raceSecPerKm);
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

    /// <summary>
    /// Average 5K-equivalent paces from Race workouts in the recent window, then suggest.
    /// </summary>
    public static CoachLayer1Paces? SuggestFromRecentRaces(
        IEnumerable<(double DistanceM, int TimeS, DateTime StartedAt)> races,
        DateTime utcNow)
    {
        var cutoff = utcNow.AddDays(-RecentRaceWindowDays);
        var equivalentPaces = races
            .Where(r =>
                r.StartedAt >= cutoff &&
                r.DistanceM >= MinRaceDistanceM &&
                r.TimeS > 0)
            .Select(r => ToReferenceSecPerKm(r.DistanceM, r.TimeS))
            .Where(p => p > 0 && !double.IsNaN(p) && !double.IsInfinity(p))
            .ToList();

        if (equivalentPaces.Count == 0)
        {
            return null;
        }

        return SuggestFromRacePace(equivalentPaces.Average());
    }

    /// <summary>
    /// Riegel-normalize a race to reference-distance equivalent pace (sec/km).
    /// </summary>
    public static double ToReferenceSecPerKm(double distanceM, int timeS)
    {
        if (distanceM < MinRaceDistanceM || timeS <= 0)
        {
            return 0;
        }

        var equivalentTimeS = timeS * Math.Pow(ReferenceDistanceM / distanceM, RiegelExponent);
        return equivalentTimeS / (ReferenceDistanceM / 1000.0);
    }

    private static int RoundPace(double secPerKm) =>
        Math.Max(1, (int)Math.Round(secPerKm, MidpointRounding.AwayFromZero));
}
