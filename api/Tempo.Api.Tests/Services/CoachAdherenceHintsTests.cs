using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachAdherenceHintsTests
{
    [Fact]
    public void Build_WhenNoPlan_ReturnsEmpty()
    {
        var hints = CoachAdherenceHints.Build(
            plannedDays: null,
            actualRuns: [new CoachAdherenceHints.ActualRun(new DateOnly(2026, 10, 13), 5000)]);

        hints.Should().BeEmpty();
    }

    [Fact]
    public void Build_MissedPlannedRun_ProducesHint()
    {
        var plan = new[]
        {
            new CoachAdherenceHints.PlannedDay(new DateOnly(2026, 10, 13), "easy", 8000)
        };

        var hints = CoachAdherenceHints.Build(plan, actualRuns: []);

        hints.Should().ContainSingle(h => h.Code == "missed_run");
    }

    [Fact]
    public void Build_ShortDistance_ProducesHint()
    {
        var day = new DateOnly(2026, 10, 13);
        var plan = new[]
        {
            new CoachAdherenceHints.PlannedDay(day, "easy", 10000)
        };
        var actual = new[]
        {
            new CoachAdherenceHints.ActualRun(day, 5000)
        };

        var hints = CoachAdherenceHints.Build(plan, actual);

        hints.Should().ContainSingle(h => h.Code == "short_distance");
    }

    [Fact]
    public void Build_ExtraOnRest_ProducesHint()
    {
        var day = new DateOnly(2026, 10, 14);
        var plan = new[]
        {
            new CoachAdherenceHints.PlannedDay(day, "rest", null)
        };
        var actual = new[]
        {
            new CoachAdherenceHints.ActualRun(day, 5000)
        };

        var hints = CoachAdherenceHints.Build(plan, actual);

        hints.Should().ContainSingle(h => h.Code == "extra_on_rest");
    }
}
