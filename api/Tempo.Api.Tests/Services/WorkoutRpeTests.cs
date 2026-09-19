using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class WorkoutRpeTests
{
    [Theory]
    [InlineData(10, (byte)1)]
    [InlineData(70, (byte)7)]
    [InlineData(100, (byte)10)]
    public void FromFitSessionRaw_DecodesExactTens(int raw, byte expected)
    {
        WorkoutRpe.FromFitSessionRaw(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(55)]
    [InlineData(255)]
    [InlineData(9)]
    [InlineData(110)]
    public void FromFitSessionRaw_RejectsInvalidOrMissing(int? raw)
    {
        WorkoutRpe.FromFitSessionRaw(raw).Should().BeNull();
    }
}
