using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CadenceTests
{
    [Fact]
    public void StepsPerMinuteFromFit_Null_ReturnsNull()
    {
        Cadence.StepsPerMinuteFromFit(null).Should().BeNull();
    }

    [Fact]
    public void StepsPerMinuteFromFit_80_Returns160()
    {
        Cadence.StepsPerMinuteFromFit(80).Should().Be(160);
    }

    [Fact]
    public void StepsPerMinuteFromFit_127_Returns254()
    {
        Cadence.StepsPerMinuteFromFit(127).Should().Be(254);
    }

    [Fact]
    public void StepsPerMinuteFromFit_128_ClampsTo255()
    {
        Cadence.StepsPerMinuteFromFit(128).Should().Be(255);
    }
}
