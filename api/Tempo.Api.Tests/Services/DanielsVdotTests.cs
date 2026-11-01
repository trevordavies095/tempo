using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for DanielsVdot against Jack Daniels' published VDOT table (Daniels' Running Formula)
/// </summary>
public class DanielsVdotTests
{
    [Theory]
    [InlineData(5000, 1448, 40, 306)]  // VDOT 40: 5K 24:08, T 5:06/km
    [InlineData(10000, 3003, 40, 306)] // VDOT 40: 10K 50:03, T 5:06/km
    [InlineData(5000, 1197, 50, 255)]  // VDOT 50: 5K 19:57, T 4:15/km
    [InlineData(10000, 2481, 50, 255)] // VDOT 50: 10K 41:21, T 4:15/km
    [InlineData(5000, 1023, 60, 220)]  // VDOT 60: 5K 17:03, T 3:40/km
    public void Vdot_AndThresholdPace_MatchPublishedTable(
        double distanceM, int timeS, double expectedVdot, int expectedThresholdPaceS)
    {
        var vdot = DanielsVdot.Vdot(distanceM, timeS);

        vdot.Should().BeApproximately(expectedVdot, 0.1);
        DanielsVdot.ThresholdPaceSecondsPerKm(vdot).Should().BeApproximately(expectedThresholdPaceS, 2);
    }

    [Fact]
    public void ThresholdPace_For20Minute5K_IsAbout4m15sPerKm()
    {
        var vdot = DanielsVdot.Vdot(5000, 1200);

        vdot.Should().BeApproximately(49.8, 0.1);
        DanielsVdot.ThresholdPaceSecondsPerKm(vdot).Should().BeApproximately(255, 2);
    }

    [Theory]
    [InlineData(0, 1200)]
    [InlineData(5000, 0)]
    public void Vdot_WithNonPositiveInput_Throws(double distanceM, double timeS)
    {
        var act = () => DanielsVdot.Vdot(distanceM, timeS);

        act.Should().Throw<ArgumentException>();
    }
}
