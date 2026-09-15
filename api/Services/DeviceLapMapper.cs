namespace Tempo.Api.Services;

/// <summary>
/// Device-authored lap summary from FIT LapMesg (or later HealthKit).
/// Candidates may be dropped by keep / 2+ rules before persist.
/// </summary>
public sealed class DeviceLapSummary
{
    public DateTime? StartTime { get; init; }
    public DateTime? Timestamp { get; init; }
    public double DistanceM { get; init; }
    public double? TimerS { get; init; }
    public double? ElapsedS { get; init; }
    public byte? AvgHeartRateBpm { get; init; }
    /// <summary>FIT lap_trigger name for raw JSON only; not a persist filter.</summary>
    public string? LapTrigger { get; init; }
}

/// <summary>
/// Maps device lap summaries to WorkoutSplit rows (kind = device_lap).
/// Keep if distance &gt; 0 or timer &gt; 0; persist only when 2+ kept.
/// </summary>
public static class DeviceLapMapper
{
    public static bool ShouldKeep(DeviceLapSummary lap) =>
        lap.DistanceM > 0 || (lap.TimerS.HasValue && lap.TimerS.Value > 0);

    public static List<Models.WorkoutSplit> ToDeviceLapSplits(
        IReadOnlyList<DeviceLapSummary> candidates,
        DateTime startedAtUtc,
        Guid workoutId)
    {
        var kept = candidates.Where(ShouldKeep).ToList();
        if (kept.Count < 2)
        {
            return new List<Models.WorkoutSplit>();
        }

        var splits = new List<Models.WorkoutSplit>(kept.Count);
        var cumulativeStartDistanceM = 0.0;
        var previousEndElapsedS = 0;

        for (var i = 0; i < kept.Count; i++)
        {
            var lap = kept[i];
            var (startElapsed, endElapsed) = WallBounds(
                lap, startedAtUtc, previousEndElapsedS);

            var durationS = DurationSeconds(lap, startElapsed, endElapsed);
            var paceS = lap.DistanceM > 0
                ? durationS / (lap.DistanceM / 1000.0)
                : 0;

            splits.Add(new Models.WorkoutSplit
            {
                Id = Guid.NewGuid(),
                WorkoutId = workoutId,
                Kind = Models.WorkoutSplitKinds.DeviceLap,
                Idx = i,
                DistanceM = lap.DistanceM,
                DurationS = durationS,
                PaceS = paceS,
                StartElapsedS = startElapsed,
                EndElapsedS = endElapsed,
                StartDistanceM = cumulativeStartDistanceM,
                AvgHeartRateBpm = lap.AvgHeartRateBpm
            });

            cumulativeStartDistanceM += lap.DistanceM;
            previousEndElapsedS = endElapsed;
        }

        AbutElapsedWindows(splits);
        return splits;
    }

    /// <summary>
    /// After series HR join (which nulls AvgHeartRateBpm), restore device averages by Idx.
    /// </summary>
    public static void OverlayDeviceAvgHeartRate(
        IList<Models.WorkoutSplit> deviceLaps,
        IReadOnlyList<DeviceLapSummary> candidates)
    {
        var kept = candidates.Where(ShouldKeep).ToList();
        if (kept.Count < 2)
        {
            return;
        }

        for (var i = 0; i < deviceLaps.Count && i < kept.Count; i++)
        {
            if (kept[i].AvgHeartRateBpm.HasValue)
            {
                deviceLaps[i].AvgHeartRateBpm = kept[i].AvgHeartRateBpm;
            }
        }
    }

    private static int DurationSeconds(DeviceLapSummary lap, int startElapsed, int endElapsed)
    {
        if (lap.TimerS.HasValue && lap.TimerS.Value > 0)
        {
            return (int)Math.Round(lap.TimerS.Value);
        }

        if (lap.ElapsedS.HasValue && lap.ElapsedS.Value > 0)
        {
            return (int)Math.Round(lap.ElapsedS.Value);
        }

        var gap = endElapsed - startElapsed;
        return gap > 0 ? gap : 0;
    }

    private static (int StartElapsedS, int EndElapsedS) WallBounds(
        DeviceLapSummary lap,
        DateTime startedAtUtc,
        int previousEndElapsedS)
    {
        int startElapsed;
        if (lap.StartTime.HasValue)
        {
            startElapsed = ElapsedFromStart(lap.StartTime.Value, startedAtUtc);
        }
        else
        {
            startElapsed = previousEndElapsedS;
        }

        int endElapsed;
        if (lap.ElapsedS.HasValue && lap.ElapsedS.Value > 0)
        {
            endElapsed = startElapsed + (int)Math.Round(lap.ElapsedS.Value);
        }
        else if (lap.Timestamp.HasValue)
        {
            endElapsed = ElapsedFromStart(lap.Timestamp.Value, startedAtUtc);
        }
        else
        {
            endElapsed = startElapsed;
        }

        if (endElapsed < startElapsed)
        {
            endElapsed = startElapsed;
        }

        return (startElapsed, endElapsed);
    }

    private static int ElapsedFromStart(DateTime pointTime, DateTime startedAt) =>
        (int)(pointTime.ToUniversalTime() - startedAt.ToUniversalTime()).TotalSeconds;

    private static void AbutElapsedWindows(List<Models.WorkoutSplit> splits)
    {
        for (var i = 1; i < splits.Count; i++)
        {
            splits[i].StartElapsedS = splits[i - 1].EndElapsedS;
        }
    }
}
