namespace Tempo.Api.Services;

/// <summary>
/// Light adherence hints comparing a committed coach plan to actual Tempo runs.
/// Athlete notes remain authoritative for intentional deviations (caller surfaces that).
/// </summary>
public static class CoachAdherenceHints
{
    public sealed record PlannedDay(DateOnly Date, string DayType, double? PlannedDistanceM);

    public sealed record ActualRun(DateOnly LocalDate, double DistanceM);

    public sealed record Hint(string Code, string Message, DateOnly? Date);

    /// <summary>
    /// Builds light hints: missed planned run, short distance, or extra run on a rest day.
    /// Returns empty when there is no plan.
    /// </summary>
    public static IReadOnlyList<Hint> Build(
        IReadOnlyList<PlannedDay>? plannedDays,
        IReadOnlyList<ActualRun> actualRuns)
    {
        if (plannedDays == null || plannedDays.Count == 0)
        {
            return [];
        }

        var hints = new List<Hint>();
        var runsByDate = actualRuns
            .GroupBy(r => r.LocalDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var day in plannedDays.OrderBy(d => d.Date))
        {
            var isRest = IsRestDay(day.DayType);
            runsByDate.TryGetValue(day.Date, out var runs);
            var runCount = runs?.Count ?? 0;
            var totalDistance = runs?.Sum(r => r.DistanceM) ?? 0;

            if (!isRest && runCount == 0)
            {
                hints.Add(new Hint(
                    "missed_run",
                    $"No run logged on {day.Date:yyyy-MM-dd} (planned {NormalizeDayType(day.DayType)}).",
                    day.Date));
                continue;
            }

            if (isRest && runCount > 0)
            {
                hints.Add(new Hint(
                    "extra_on_rest",
                    $"Run logged on rest day {day.Date:yyyy-MM-dd}.",
                    day.Date));
                continue;
            }

            if (!isRest
                && day.PlannedDistanceM is > 0
                && totalDistance > 0
                && totalDistance < day.PlannedDistanceM.Value * 0.85)
            {
                hints.Add(new Hint(
                    "short_distance",
                    $"Shorter than planned on {day.Date:yyyy-MM-dd} ({totalDistance:0} m vs {day.PlannedDistanceM.Value:0} m).",
                    day.Date));
            }
        }

        return hints;
    }

    private static bool IsRestDay(string? dayType)
    {
        var t = NormalizeDayType(dayType);
        return t is "rest" or "off" or "none";
    }

    private static string NormalizeDayType(string? dayType)
        => (dayType ?? string.Empty).Trim().ToLowerInvariant();
}
