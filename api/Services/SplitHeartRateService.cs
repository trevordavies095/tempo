using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Time-weighted average heart rate per WorkoutSplit from WorkoutTimeSeries.
/// Does not invent values from workout-level averages.
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

        var orderedSplits = splits.OrderBy(s => s.Idx).ToList();
        var distanceEnds = new double[orderedSplits.Count];
        var durationEnds = new double[orderedSplits.Count];
        var distanceCum = 0.0;
        var durationCum = 0.0;
        for (var i = 0; i < orderedSplits.Count; i++)
        {
            distanceCum += orderedSplits[i].DistanceM;
            durationCum += orderedSplits[i].DurationS;
            distanceEnds[i] = distanceCum;
            durationEnds[i] = durationCum;
        }

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

            var splitIndex = current.DistanceM.HasValue
                ? IndexForWindow(current.DistanceM.Value, distanceEnds)
                : IndexForWindow(current.ElapsedSeconds, durationEnds);

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
    /// Windows are [0, e0), [e0, e1), …; the last window is inclusive of its end and any remainder past it.
    /// </summary>
    private static int IndexForWindow(double value, IReadOnlyList<double> ends)
    {
        for (var i = 0; i < ends.Count; i++)
        {
            var start = i == 0 ? 0.0 : ends[i - 1];
            var isLast = i == ends.Count - 1;
            if (value >= start && (value < ends[i] || isLast))
            {
                return i;
            }
        }

        return -1;
    }
}
