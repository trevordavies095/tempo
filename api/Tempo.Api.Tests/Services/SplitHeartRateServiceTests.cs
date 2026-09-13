using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class SplitHeartRateServiceTests
{
    private readonly SplitHeartRateService _service = new();

    [Fact]
    public void ApplyToSplits_DistanceWindows_WritesTimeWeightedAverage()
    {
        var splits = CreateSplits(
            (0, 1000, 300),
            (1, 1000, 300));
        var series = new List<WorkoutTimeSeries>
        {
            Sample(elapsed: 0, hr: 140, distanceM: 0),
            Sample(elapsed: 1, hr: 160, distanceM: 500),
            Sample(elapsed: 2, hr: 180, distanceM: 1000),
            Sample(elapsed: 3, hr: 150, distanceM: 1500)
        };

        _service.ApplyToSplits(splits, series);

        // Split 0: 140*1 + 160*1 = 300 / 2 = 150 (1000m sample is start of split 1)
        splits[0].AvgHeartRateBpm.Should().Be(150);
        // Split 1: 180*1 + 150*1 = 330 / 2 = 165
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
            Sample(elapsed: 0, hr: null, distanceM: 100),
            Sample(elapsed: 1, hr: null, distanceM: 1500)
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
            Sample(elapsed: 0, hr: 140, distanceM: 100),
            Sample(elapsed: 1, hr: null, distanceM: 200),
            Sample(elapsed: 2, hr: 160, distanceM: 300)
        };

        _service.ApplyToSplits(splits, series);

        // 140*1 (to next row) + 160*1 (last sample) = 150
        splits[0].AvgHeartRateBpm.Should().Be(150);
    }

    private static List<WorkoutSplit> CreateSplits(params (int Idx, double DistanceM, int DurationS)[] rows)
    {
        return rows.Select(r => new WorkoutSplit
        {
            Idx = r.Idx,
            DistanceM = r.DistanceM,
            DurationS = r.DurationS,
            PaceS = r.DurationS / (r.DistanceM / 1000.0)
        }).ToList();
    }

    private static WorkoutTimeSeries Sample(int elapsed, byte? hr, double? distanceM) => new()
    {
        Id = Guid.NewGuid(),
        ElapsedSeconds = elapsed,
        HeartRateBpm = hr,
        DistanceM = distanceM
    };
}
