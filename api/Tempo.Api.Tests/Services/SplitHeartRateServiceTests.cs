using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class SplitHeartRateServiceTests
{
    private readonly SplitHeartRateService _service = new();

    [Fact]
    public void ApplyToSplits_ElapsedWindows_WritesTimeWeightedAverage()
    {
        var splits = CreateSplits(
            (0, 1000, 300),
            (1, 1000, 300));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: 140),
            Sample(elapsed: 1, hr: 160),
            Sample(elapsed: 300, hr: 180),
            Sample(elapsed: 301, hr: 150)
        };

        _service.ApplyToSplits(splits, series);

        // Split 0 [0, 300): 140*1 + 160*1 = 150 (300 is start of split 1)
        splits[0].AvgHeartRateBpm.Should().Be(150);
        // Split 1 [300, 600]: 180*1 + 150*1 = 165
        splits[1].AvgHeartRateBpm.Should().Be(165);
    }

    [Fact]
    public void ApplyToSplits_EmptySeries_LeavesAllSplitsNull()
    {
        var splits = CreateSplits((0, 1000, 300));

        _service.ApplyToSplits(splits, Array.Empty<WorkoutTimeSeries>());

        splits.Should().OnlyContain(s => s.AvgHeartRateBpm == null);
    }

    [Fact]
    public void ApplyToSplits_SeriesWithoutHeartRate_LeavesAllSplitsNull()
    {
        var splits = CreateSplits((0, 1000, 300), (1, 1000, 300));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: null),
            Sample(elapsed: 1, hr: null)
        };

        _service.ApplyToSplits(splits, series);

        splits.Should().OnlyContain(s => s.AvgHeartRateBpm == null);
    }

    [Fact]
    public void ApplyToSplits_SkipsNullHeartRateSamples()
    {
        var splits = CreateSplits((0, 1000, 300));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: 140),
            Sample(elapsed: 1, hr: null),
            Sample(elapsed: 2, hr: 160)
        };

        _service.ApplyToSplits(splits, series);

        // 140*1 (to next row) + 160*1 (last sample) = 150
        splits[0].AvgHeartRateBpm.Should().Be(150);
    }

    [Fact]
    public void ApplyToSplits_StrapStartsMidRun_LeavesEarlySplitsNull()
    {
        var splits = CreateSplits(
            (0, 1000, 300),
            (1, 1000, 300),
            (2, 1000, 300));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: null),
            Sample(elapsed: 250, hr: null),
            Sample(elapsed: 650, hr: 158),
            Sample(elapsed: 700, hr: 162)
        };

        _service.ApplyToSplits(splits, series);

        splits[0].AvgHeartRateBpm.Should().BeNull();
        splits[1].AvgHeartRateBpm.Should().BeNull();
        // 158*1 + 162*1 = 160
        splits[2].AvgHeartRateBpm.Should().Be(160);
    }

    [Fact]
    public void ApplyToSplits_AssignByElapsed_IgnoresSampleDistance()
    {
        var splits = CreateSplits(
            (0, 1000, 300),
            (1, 1000, 300));
        var series = new List<WorkoutTimeSeries>
        {
            // Distance would put both in split 0; elapsed puts second in split 1.
            Sample(elapsed: 0, hr: 140, distanceM: 100),
            Sample(elapsed: 1, hr: 160, distanceM: 200),
            Sample(elapsed: 301, hr: 180, distanceM: 250),
            Sample(elapsed: 302, hr: 200, distanceM: 300)
        };

        _service.ApplyToSplits(splits, series);

        splits[0].AvgHeartRateBpm.Should().Be(150);
        splits[1].AvgHeartRateBpm.Should().Be(190);
    }

    [Fact]
    public void ApplyToSplits_GapGreaterThan10Seconds_ClampsWeightToOneSecond()
    {
        var splits = CreateSplits((0, 1000, 300));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: 100),
            Sample(elapsed: 50, hr: 200)
        };

        _service.ApplyToSplits(splits, series);

        // Unclamped would be (100*50 + 200*1) / 51 ≈ 102; clamp makes 150
        splits[0].AvgHeartRateBpm.Should().Be(150);
    }

    [Fact]
    public void ApplyToSplits_LastSample_WeightsOneSecond()
    {
        var splits = CreateSplits((0, 1000, 300));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: 100),
            Sample(elapsed: 2, hr: 200)
        };

        _service.ApplyToSplits(splits, series);

        // 100*2 + 200*1 = 400 / 3 = 133
        splits[0].AvgHeartRateBpm.Should().Be(133);
    }

    [Fact]
    public void ApplyToSplits_RemainderSplit_UsesOwnWindowOnly()
    {
        var splits = CreateSplits(
            (0, 1000, 300),
            (1, 1000, 300),
            (2, 200, 60));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: 140),
            Sample(elapsed: 1, hr: 160),
            Sample(elapsed: 300, hr: 170),
            Sample(elapsed: 301, hr: 190)
        };

        _service.ApplyToSplits(splits, series);

        splits[0].AvgHeartRateBpm.Should().Be(150);
        splits[1].AvgHeartRateBpm.Should().Be(180);
        splits[2].AvgHeartRateBpm.Should().BeNull();
        splits[2].AvgHeartRateBpm.Should().NotBe(splits[1].AvgHeartRateBpm);
    }

    [Fact]
    public void ApplyToSplits_RemainderSplit_AveragesItsOwnSamples()
    {
        var splits = CreateSplits(
            (0, 1000, 300),
            (1, 200, 60));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: 140),
            Sample(elapsed: 1, hr: 160),
            Sample(elapsed: 301, hr: 200)
        };

        _service.ApplyToSplits(splits, series);

        splits[0].AvgHeartRateBpm.Should().Be(150);
        splits[1].AvgHeartRateBpm.Should().Be(200);
    }

    [Fact]
    public void ApplyToSplits_IndoorElapsedWindows_WritesAverage()
    {
        var splits = CreateSplits(
            (0, 1000, 360),
            (1, 1000, 360));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: 130),
            Sample(elapsed: 1, hr: 150),
            Sample(elapsed: 360, hr: 160),
            Sample(elapsed: 361, hr: 180)
        };

        _service.ApplyToSplits(splits, series);

        splits[0].AvgHeartRateBpm.Should().Be(140);
        splits[1].AvgHeartRateBpm.Should().Be(170);
    }

    [Fact]
    public void ApplyToSplits_DoesNotUseWorkoutLevelHeartRate()
    {
        var splits = CreateSplits((0, 1000, 300), (1, 1000, 300));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: null),
            Sample(elapsed: 400, hr: null)
        };

        _service.ApplyToSplits(splits, series);

        splits.Should().OnlyContain(s => s.AvgHeartRateBpm == null);
    }

    private static List<WorkoutSplit> CreateSplits(params (int Idx, double DistanceM, int DurationS)[] rows)
    {
        var splits = rows.Select(r => new WorkoutSplit
        {
            Id = Guid.NewGuid(),
            Kind = WorkoutSplitKinds.Distance,
            Idx = r.Idx,
            DistanceM = r.DistanceM,
            DurationS = r.DurationS,
            PaceS = r.DurationS / (r.DistanceM / 1000.0)
        }).ToList();

        WorkoutSplitElapsed.FillFromCumulativeDuration(splits);
        return splits;
    }

    private static WorkoutTimeSeries Sample(int elapsed, byte? hr, double? distanceM = null) => new()
    {
        Id = Guid.NewGuid(),
        ElapsedSeconds = elapsed,
        HeartRateBpm = hr,
        DistanceM = distanceM
    };
}
