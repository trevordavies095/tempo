using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachAttachedRunBuilderTests
{
    [Fact]
    public void DownsampleSeries_Empty_ReturnsEmpty()
    {
        CoachAttachedRunBuilder.DownsampleSeries([], 90).Should().BeEmpty();
    }

    [Fact]
    public void DownsampleSeries_FewerThanTarget_KeepsAll()
    {
        var source = Enumerable.Range(0, 10)
            .Select(i => new WorkoutTimeSeries
            {
                ElapsedSeconds = i * 10,
                HeartRateBpm = (byte)(140 + i),
                SpeedMps = 3.0,
                CadenceRpm = 170,
                ElevationM = 100 + i,
                GradePercent = 1.0
            })
            .ToList();

        var result = CoachAttachedRunBuilder.DownsampleSeries(source, 90);
        result.Should().HaveCount(10);
        result[0].ElapsedS.Should().Be(0);
        result[^1].ElapsedS.Should().Be(90);
        result.Should().OnlyContain(p =>
            p.HeartRateBpm != null
            && p.PaceSPerKm != null
            && p.CadenceSpm != null
            && p.ElevationM != null
            && p.GradePercent != null);
    }

    [Fact]
    public void DownsampleSeries_ManyPoints_TargetsCountAndEndpoints()
    {
        var source = Enumerable.Range(0, 1000)
            .Select(i => new WorkoutTimeSeries
            {
                ElapsedSeconds = i,
                HeartRateBpm = 150,
                SpeedMps = 3.5,
                ElevationM = 50
            })
            .ToList();

        var result = CoachAttachedRunBuilder.DownsampleSeries(source, 90);
        result.Count.Should().BeLessThanOrEqualTo(90);
        result.Count.Should().BeGreaterThanOrEqualTo(2);
        result[0].ElapsedS.Should().Be(0);
        result[^1].ElapsedS.Should().Be(999);
        // Typed series points have no lat/lng properties — GPS omitted by construction.
        result[0].GetType().GetProperty("Lat").Should().BeNull();
        result[0].GetType().GetProperty("Lng").Should().BeNull();
        result[0].GetType().GetProperty("Latitude").Should().BeNull();
    }

    [Fact]
    public void ComputeHrDriftBpm_RequiresEnoughSamples()
    {
        var few = Enumerable.Range(0, 3)
            .Select(i => new WorkoutTimeSeries { ElapsedSeconds = i, HeartRateBpm = 140 })
            .ToList();
        CoachAttachedRunBuilder.ComputeHrDriftBpm(few).Should().BeNull();
    }

    [Fact]
    public void ComputeHrDriftBpm_SecondHalfMinusFirstHalf()
    {
        var series = new List<WorkoutTimeSeries>();
        for (var i = 0; i < 10; i++)
        {
            series.Add(new WorkoutTimeSeries
            {
                ElapsedSeconds = i,
                HeartRateBpm = (byte)(i < 5 ? 140 : 150)
            });
        }

        var drift = CoachAttachedRunBuilder.ComputeHrDriftBpm(series);
        drift.Should().Be(10.0);
    }

    [Fact]
    public void ComputeDewPointC_NullWhenIncomplete()
    {
        CoachAttachedRunBuilder.ComputeDewPointC(null, 60).Should().BeNull();
        CoachAttachedRunBuilder.ComputeDewPointC(20, null).Should().BeNull();
        CoachAttachedRunBuilder.ComputeDewPointC(20, 0).Should().BeNull();
    }

    [Fact]
    public void ComputeDewPointC_MagnusAtKnownPoint()
    {
        // ~20°C / 50% RH → dew point ≈ 9.3°C
        var dew = CoachAttachedRunBuilder.ComputeDewPointC(20, 50);
        dew.Should().NotBeNull();
        dew!.Value.Should().BeApproximately(9.3, 0.5);
    }

    [Fact]
    public void ParseAmbientWeather_DerivesDewPoint_OmitsDeviceTempKeys()
    {
        var json = """{"temperature":18.0,"humidity":70,"windSpeed":3.2,"source":"open-meteo"}""";
        var weather = CoachAttachedRunBuilder.ParseAmbientWeather(json);
        weather.Should().NotBeNull();
        weather!.TemperatureC.Should().Be(18.0);
        weather.HumidityPercent.Should().Be(70);
        weather.DewPointC.Should().NotBeNull();
        weather.Source.Should().Be("open-meteo");
    }

    [Fact]
    public void EnrichSplits_AveragesCadenceAndElevFromSeriesBounds()
    {
        var splits = new List<WorkoutSplit>
        {
            new()
            {
                Idx = 0,
                DistanceM = 1000,
                DurationS = 300,
                PaceS = 300,
                AvgHeartRateBpm = 145,
                StartElapsedS = 0,
                EndElapsedS = 300
            },
            new()
            {
                Idx = 1,
                DistanceM = 1000,
                DurationS = 310,
                PaceS = 310,
                AvgHeartRateBpm = 150,
                StartElapsedS = 300,
                EndElapsedS = 610
            }
        };

        var series = new List<WorkoutTimeSeries>();
        for (var t = 0; t < 610; t += 10)
        {
            series.Add(new WorkoutTimeSeries
            {
                ElapsedSeconds = t,
                CadenceRpm = (byte)(t < 300 ? 160 : 172),
                ElevationM = t < 300 ? 100 : 120
            });
        }

        var enriched = CoachAttachedRunBuilder.EnrichSplits(splits, series);
        enriched.Should().HaveCount(2);
        enriched[0].AvgHeartRateBpm.Should().Be(145);
        enriched[0].PaceSPerKm.Should().Be(300);
        enriched[0].AvgCadenceSpm.Should().Be(160);
        enriched[0].AvgElevM.Should().Be(100);
        enriched[1].AvgCadenceSpm.Should().Be(172);
        enriched[1].AvgElevM.Should().Be(120);
    }

    [Fact]
    public void LooksLikeResidualGps_DetectsCoordinatePairsInJson()
    {
        CoachPrivacyRedactor.LooksLikeResidualGps("""{"series":[{"lat":42.3,"lng":-71.1}]}""")
            .Should().BeTrue();
        CoachPrivacyRedactor.LooksLikeResidualGps("""{"series":[{"elapsedS":0,"heartRateBpm":140}]}""")
            .Should().BeFalse();
    }
}
