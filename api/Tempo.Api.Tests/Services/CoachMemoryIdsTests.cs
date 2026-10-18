using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachMemoryIdsTests
{
    [Fact]
    public void EpisodeId_SlugsAndDeduplicates()
    {
        var date = new DateOnly(2026, 9, 20);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        var first = CoachMemoryIds.EpisodeId(date, "race", "Rochester Half: textbook pace", existing);
        first.Should().StartWith("ep_2026-09-20_race_");
        existing.Add(first);

        var second = CoachMemoryIds.EpisodeId(date, "race", "Rochester Half: textbook pace", existing);
        second.Should().Be(first + "_2");
    }

    [Fact]
    public void RuleId_SlugsFromRuleText()
    {
        var id = CoachMemoryIds.RuleId("Afternoon MP costs more recovery", new HashSet<string>());
        id.Should().StartWith("rule_");
        id.Should().Contain("afternoon");
    }

    [Fact]
    public void Slugify_ReplacesNonAlnum()
    {
        CoachMemoryIds.Slugify("Hello, World!!", 40).Should().Be("hello_world");
    }
}
