using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachRecentTrainingRollupsTests
{
    [Fact]
    public void MedianPositiveRunCount_Empty_ReturnsNull()
    {
        CoachRecentTrainingRollups.MedianPositiveRunCount([]).Should().BeNull();
    }

    [Fact]
    public void MedianPositiveRunCount_Odd_ReturnsMiddle()
    {
        CoachRecentTrainingRollups.MedianPositiveRunCount([5, 3, 4]).Should().Be(4);
    }

    [Fact]
    public void MedianPositiveRunCount_Even_AveragesMiddlePair()
    {
        CoachRecentTrainingRollups.MedianPositiveRunCount([3, 5]).Should().Be(4);
        CoachRecentTrainingRollups.MedianPositiveRunCount([2, 5]).Should().Be(4); // 3.5 → AwayFromZero → 4
    }
}
