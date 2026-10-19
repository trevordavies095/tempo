using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachPaceFormatTests
{
    [Fact]
    public void FormatSecPerKm_Imperial_402_Is_10_47_PerMi()
    {
        CoachPaceFormat.FormatSecPerKm(402, "imperial").Should().Be("10:47 /mi");
    }

    [Fact]
    public void FormatSecPerKm_Metric_402_Is_6_42_PerKm()
    {
        CoachPaceFormat.FormatSecPerKm(402, "metric").Should().Be("6:42 /km");
    }

    [Fact]
    public void FormatSecPerKm_NonPositive_ReturnsNull()
    {
        CoachPaceFormat.FormatSecPerKm(0, "imperial").Should().BeNull();
        CoachPaceFormat.FormatSecPerKm(-10, "metric").Should().BeNull();
    }

    [Fact]
    public void FormatFromDistanceAndDuration_MatchesSecPerKm()
    {
        // 5 km in 1800s → 360 sec/km → 9:39 /mi
        CoachPaceFormat.FormatFromDistanceAndDuration(5000, 1800, "imperial")
            .Should().Be(CoachPaceFormat.FormatSecPerKm(360, "imperial"));
    }

    [Fact]
    public void FromLayer1Paces_Imperial_FillsDisplayFields()
    {
        var display = CoachPaceFormat.FromLayer1Paces(
            new CoachLayer1Paces(402, 340, 309, 294),
            "imperial");

        display.Unit.Should().Be("/mi");
        display.Easy.Should().Be("10:47 /mi");
        display.Tempo.Should().Be("9:07 /mi");
        display.Threshold.Should().Be("8:17 /mi");
        display.Interval.Should().Be("7:53 /mi");
    }
}
