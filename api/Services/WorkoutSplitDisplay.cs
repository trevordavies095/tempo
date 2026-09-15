using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Display list for Workout overview / GET: device_lap if any exist, else distance.
/// Never mixes kinds in one list.
/// </summary>
public static class WorkoutSplitDisplay
{
    public static IReadOnlyList<WorkoutSplit> SelectDisplayList(IEnumerable<WorkoutSplit> splits)
    {
        var list = splits as IList<WorkoutSplit> ?? splits.ToList();
        if (list.Count == 0)
        {
            return Array.Empty<WorkoutSplit>();
        }

        if (list.Any(s => s.Kind == WorkoutSplitKinds.DeviceLap))
        {
            return list
                .Where(s => s.Kind == WorkoutSplitKinds.DeviceLap)
                .OrderBy(s => s.Idx)
                .ToList();
        }

        return list
            .Where(s => s.Kind == WorkoutSplitKinds.Distance)
            .OrderBy(s => s.Idx)
            .ToList();
    }
}
