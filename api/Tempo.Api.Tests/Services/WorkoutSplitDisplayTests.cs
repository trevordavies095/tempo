using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class WorkoutSplitDisplayTests
{
    [Fact]
    public void SelectDisplayList_DistanceOnly_ReturnsDistanceOrderedByIdx()
    {
        var splits = new[]
        {
            Distance(2, 200),
            Distance(0, 300),
            Distance(1, 310)
        };

        var display = WorkoutSplitDisplay.SelectDisplayList(splits);

        display.Should().HaveCount(3);
        display.Select(s => s.Idx).Should().Equal(0, 1, 2);
        display.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.Distance);
    }

    [Fact]
    public void SelectDisplayList_BothKinds_ReturnsDeviceLapsOnly()
    {
        var splits = new[]
        {
            Distance(0, 300),
            Distance(1, 310),
            Lap(0, 600, 0, 650),
            Lap(1, 620, 650, 1300)
        };

        var display = WorkoutSplitDisplay.SelectDisplayList(splits);

        display.Should().HaveCount(2);
        display.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.DeviceLap);
        display.Select(s => s.Idx).Should().Equal(0, 1);
    }

    [Fact]
    public void SelectDisplayList_Empty_ReturnsEmpty()
    {
        WorkoutSplitDisplay.SelectDisplayList(Array.Empty<WorkoutSplit>()).Should().BeEmpty();
    }

    private static WorkoutSplit Distance(int idx, int durationS) => new()
    {
        Id = Guid.NewGuid(),
        Kind = WorkoutSplitKinds.Distance,
        Idx = idx,
        DistanceM = 1000,
        DurationS = durationS,
        PaceS = durationS,
        StartElapsedS = idx * durationS,
        EndElapsedS = (idx + 1) * durationS
    };

    private static WorkoutSplit Lap(int idx, int durationS, int start, int end) => new()
    {
        Id = Guid.NewGuid(),
        Kind = WorkoutSplitKinds.DeviceLap,
        Idx = idx,
        DistanceM = 1600,
        DurationS = durationS,
        PaceS = durationS,
        StartElapsedS = start,
        EndElapsedS = end
    };
}
