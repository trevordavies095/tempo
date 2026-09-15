using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Fills wall-elapsed bounds from cumulative DurationS (migration / old export restore).
/// Windows abut: next StartElapsedS equals previous EndElapsedS.
/// </summary>
public static class WorkoutSplitElapsed
{
    public static void FillFromCumulativeDuration(IList<WorkoutSplit> splits)
    {
        if (splits.Count == 0)
        {
            return;
        }

        var ordered = splits
            .OrderBy(s => s.Idx)
            .ThenBy(s => s.Id)
            .ToList();

        var elapsed = 0;
        foreach (var split in ordered)
        {
            if (string.IsNullOrWhiteSpace(split.Kind))
            {
                split.Kind = WorkoutSplitKinds.Distance;
            }

            split.StartElapsedS = elapsed;
            elapsed += split.DurationS;
            split.EndElapsedS = elapsed;
        }
    }

    /// <summary>
    /// True when every row looks like a pre-kind export (missing/zero elapsed with positive duration).
    /// </summary>
    public static bool NeedsCumulativeFill(IReadOnlyList<WorkoutSplit> splits)
    {
        if (splits.Count == 0)
        {
            return false;
        }

        var anyPositiveDuration = splits.Any(s => s.DurationS > 0);
        if (!anyPositiveDuration)
        {
            return false;
        }

        return splits.All(s => s.EndElapsedS == 0);
    }
}
