namespace Tempo.Api.Services;

/// <summary>
/// In-memory week rollups matching StatsEndpoints weekly-recap bucket semantics
/// (elevation COALESCE 0; relative effort sums non-null only; easy-run duration-weighted avg HR).
/// </summary>
public static class CoachRecentTrainingRollups
{
    public const string EasyRunType = "Easy Run";

    public readonly record struct WorkoutRow(
        Guid Id,
        DateTime StartedAt,
        double DistanceM,
        int DurationS,
        double? ElevGainM,
        int? RelativeEffort,
        byte? AvgHeartRateBpm,
        byte? MaxHeartRateBpm,
        byte? Rpe,
        Guid? ShoeId,
        string? RunType,
        string? Notes);

    public readonly record struct WeekRollup(
        int RunCount,
        double DistanceM,
        long DurationS,
        double ElevationGainM,
        int RelativeEffortSum,
        double? EasyRunAvgHeartRateBpm);

    public static WeekRollup Compute(IReadOnlyList<WorkoutRow> workouts)
    {
        if (workouts.Count == 0)
        {
            return new WeekRollup(0, 0, 0, 0, 0, null);
        }

        var runCount = workouts.Count;
        var distanceM = workouts.Sum(w => w.DistanceM);
        var durationS = workouts.Sum(w => (long)w.DurationS);
        var elevationGainM = workouts.Sum(w => w.ElevGainM ?? 0.0);
        var relativeEffortSum = workouts.Where(w => w.RelativeEffort.HasValue).Sum(w => w.RelativeEffort!.Value);

        double? easyHrAvg = null;
        long easyDur = 0;
        double easyWeighted = 0;
        foreach (var w in workouts)
        {
            if (w.RunType == EasyRunType && w.AvgHeartRateBpm != null && w.DurationS > 0)
            {
                easyDur += w.DurationS;
                easyWeighted += (double)w.AvgHeartRateBpm.Value * w.DurationS;
            }
        }

        if (easyDur > 0)
        {
            easyHrAvg = Math.Round(easyWeighted / easyDur, 1, MidpointRounding.AwayFromZero);
        }

        return new WeekRollup(runCount, distanceM, durationS, elevationGainM, relativeEffortSum, easyHrAvg);
    }

    /// <summary>
    /// Median of positive week run counts (empty weeks already excluded).
    /// Even count: average of the two middle values, rounded AwayFromZero.
    /// </summary>
    public static int? MedianPositiveRunCount(IReadOnlyList<int> nonEmptyWeekRunCounts)
    {
        if (nonEmptyWeekRunCounts.Count == 0)
        {
            return null;
        }

        var sorted = nonEmptyWeekRunCounts.OrderBy(c => c).ToList();
        var mid = sorted.Count / 2;
        if (sorted.Count % 2 == 1)
        {
            return sorted[mid];
        }

        var avg = (sorted[mid - 1] + sorted[mid]) / 2.0;
        return (int)Math.Round(avg, MidpointRounding.AwayFromZero);
    }
}
