using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Time-weighted average heart rate per WorkoutSplit from WorkoutTimeSeries.
/// Does not invent values from workout-level averages.
/// Joins samples by wall elapsed: [StartElapsedS, EndElapsedS); last split inclusive of its end.
/// </summary>
public class SplitHeartRateService
{
    public void ApplyToSplits(IList<WorkoutSplit> splits, IReadOnlyList<WorkoutTimeSeries> series)
    {
        foreach (var split in splits)
        {
            split.AvgHeartRateBpm = null;
        }

        if (splits.Count == 0 || series.Count == 0)
        {
            return;
        }

        var orderedSplits = splits.OrderBy(s => s.Idx).ThenBy(s => s.Id).ToList();
        var orderedSeries = series
            .OrderBy(s => s.ElapsedSeconds)
            .ThenBy(s => s.Id)
            .ToList();

        var weightedSum = new double[orderedSplits.Count];
        var weight = new double[orderedSplits.Count];

        for (var i = 0; i < orderedSeries.Count; i++)
        {
            var current = orderedSeries[i];
            if (!current.HeartRateBpm.HasValue)
            {
                continue;
            }

            var timeSeconds = 1.0;
            if (i < orderedSeries.Count - 1)
            {
                timeSeconds = orderedSeries[i + 1].ElapsedSeconds - current.ElapsedSeconds;
                if (timeSeconds > 10 || timeSeconds < 0)
                {
                    timeSeconds = 1.0;
                }
            }

            var splitIndex = IndexForElapsedWindow(current.ElapsedSeconds, orderedSplits);
            if (splitIndex < 0)
            {
                continue;
            }

            weightedSum[splitIndex] += current.HeartRateBpm.Value * timeSeconds;
            weight[splitIndex] += timeSeconds;
        }

        for (var i = 0; i < orderedSplits.Count; i++)
        {
            if (weight[i] <= 0)
            {
                continue;
            }

            var rounded = (int)Math.Round(weightedSum[i] / weight[i], MidpointRounding.AwayFromZero);
            orderedSplits[i].AvgHeartRateBpm = (byte)Math.Clamp(rounded, 0, 255);
        }
    }

    /// <summary>
    /// Windows are [start, end) for all but the last split, which is inclusive of its end
    /// and any remainder past it.
    /// </summary>
    private static int IndexForElapsedWindow(double elapsedSeconds, IReadOnlyList<WorkoutSplit> orderedSplits)
    {
        for (var i = 0; i < orderedSplits.Count; i++)
        {
            var split = orderedSplits[i];
            var isLast = i == orderedSplits.Count - 1;
            if (elapsedSeconds >= split.StartElapsedS &&
                (elapsedSeconds < split.EndElapsedS || isLast))
            {
                return i;
            }
        }

        return -1;
    }
}
