using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachPaceSuggestionsTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SuggestFromRecentRaces_SingleRace_UsesThatPace()
    {
        // 5K in 1200s → 240 sec/km
        var result = CoachPaceSuggestions.SuggestFromRecentRaces(
            [(5000, 1200, Now.AddDays(-10))],
            Now);

        result.Should().NotBeNull();
        result!.ThresholdSecPerKm.Should().Be(240);
        result.EasySecPerKm.Should().Be(312);
    }

    [Fact]
    public void SuggestFromRecentRaces_AveragesMultipleRaces()
    {
        var result = CoachPaceSuggestions.SuggestFromRecentRaces(
            [
                (5000, 1200, Now.AddDays(-7)),  // 240 sec/km
                (5000, 1300, Now.AddDays(-30)) // 260 sec/km
            ],
            Now);

        result.Should().NotBeNull();
        result!.ThresholdSecPerKm.Should().Be(250); // average
        result.EasySecPerKm.Should().Be(325);
    }

    [Fact]
    public void SuggestFromRecentRaces_IgnoresOutsideWindow()
    {
        var result = CoachPaceSuggestions.SuggestFromRecentRaces(
            [
                (5000, 1200, Now.AddDays(-10)),
                (5000, 900, Now.AddDays(-200)) // ignored
            ],
            Now);

        result.Should().NotBeNull();
        result!.ThresholdSecPerKm.Should().Be(240);
    }

    [Fact]
    public void SuggestFromRecentRaces_NormalizesDistanceWithRiegel()
    {
        // 10K in 2400s is 240 sec/km raw, but 5K-equivalent is faster than 240.
        var result = CoachPaceSuggestions.SuggestFromRecentRaces(
            [(10000, 2400, Now.AddDays(-5))],
            Now);

        result.Should().NotBeNull();
        var expectedSecPerKm = CoachPaceSuggestions.ToReferenceSecPerKm(10000, 2400);
        expectedSecPerKm.Should().BeLessThan(240);
        result!.ThresholdSecPerKm.Should().Be(
            (int)Math.Round(expectedSecPerKm, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void SuggestFromRecentRaces_Empty_ReturnsNull()
    {
        CoachPaceSuggestions.SuggestFromRecentRaces([], Now).Should().BeNull();
    }
}
