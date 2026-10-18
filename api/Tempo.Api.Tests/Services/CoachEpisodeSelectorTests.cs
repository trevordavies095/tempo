using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachEpisodeSelectorTests
{
    private static readonly DateOnly WeekEnding = new(2026, 10, 18); // Sunday
    private static readonly DateOnly PriorWeekEnding = new(2026, 10, 11);

    [Fact]
    public void Select_PriorWeek_ViaSourceWeekEnding()
    {
        var episodes = new[]
        {
            Ep("ep_prior", new DateOnly(2026, 10, 10), "race", "Half result", PriorWeekEnding),
            Ep("ep_old", new DateOnly(2026, 8, 1), "milestone", "Old volume PR", null)
        };

        var result = CoachEpisodeSelector.Select(
            episodes,
            WeekEnding,
            EmptyQuery(),
            l2TopK: 8);

        result.PriorWeek.Should().ContainSingle(e => e.Id == "ep_prior");
        result.PriorWeekEnding.Should().Be(PriorWeekEnding);
        result.Retrieved.Should().NotContain(e => e.Id == "ep_prior");
    }

    [Fact]
    public void Select_PriorWeek_ViaDateFallback_WhenSourceWeekEndingMissing()
    {
        var episodes = new[]
        {
            Ep("ep_date", new DateOnly(2026, 10, 8), "issue", "Calf niggle onset", null),
            Ep("ep_outside", new DateOnly(2026, 9, 1), "milestone", "September block", null)
        };

        var result = CoachEpisodeSelector.Select(episodes, WeekEnding, EmptyQuery(), 8);

        result.PriorWeek.Should().ContainSingle(e => e.Id == "ep_date");
        result.Retrieved.Select(e => e.Id).Should().Contain("ep_outside");
    }

    [Fact]
    public void Select_ExcludesRetired()
    {
        var retired = Ep("ep_retired", new DateOnly(2026, 10, 10), "race", "Retired race", PriorWeekEnding);
        retired.Retired = true;

        var result = CoachEpisodeSelector.Select([retired], WeekEnding, EmptyQuery(), 8);

        result.PriorWeek.Should().BeEmpty();
        result.Retrieved.Should().BeEmpty();
    }

    [Fact]
    public void Select_ScoresBoostsAndCapsRetrieved()
    {
        var query = new CoachEpisodeQueryContext(
            OverallFeel: null,
            Niggles: "calf tight after long",
            LifeConstraints: "travel Thursday",
            RunNotes: ["felt calf pull"],
            ConstraintNotes: []);

        var episodes = new List<CoachEpisode>
        {
            Ep("ep_issue", new DateOnly(2026, 9, 1), "issue", "Calf niggle flare after long run", null),
            Ep("ep_race", new DateOnly(2026, 9, 2), "race", "Unrelated race result", null),
            Ep("ep_old1", new DateOnly(2026, 7, 1), "equipment", "Shoe note", null),
            Ep("ep_old2", new DateOnly(2026, 7, 2), "milestone", "Volume gate", null),
            Ep("ep_old3", new DateOnly(2026, 7, 3), "protocol_change", "Schedule tweak", null)
        };

        var result = CoachEpisodeSelector.Select(episodes, WeekEnding, query, l2TopK: 2);

        result.Retrieved.Should().HaveCount(2);
        result.Retrieved[0].Id.Should().Be("ep_issue");
        result.ScoresById.Should().ContainKey("ep_issue");
        result.L2TopK.Should().Be(2);
    }

    [Fact]
    public void Select_EmptyCheckIn_StillReturnsPriorWeek()
    {
        var episodes = new[]
        {
            Ep("ep_prior", new DateOnly(2026, 10, 9), "breakthrough", "Breakthrough tempo", PriorWeekEnding)
        };

        var result = CoachEpisodeSelector.Select(episodes, WeekEnding, EmptyQuery(), 8);

        result.PriorWeek.Should().ContainSingle();
        result.Retrieved.Should().BeEmpty();
        result.QueryTerms.Should().BeEmpty();
    }

    [Fact]
    public void Select_TieBreak_PrefersNewerDate()
    {
        var query = new CoachEpisodeQueryContext(
            null, "heat", null, [], []);

        var older = Ep("ep_old", new DateOnly(2026, 6, 1), "milestone", "heat adaptation block", null);
        var newer = Ep("ep_new", new DateOnly(2026, 8, 1), "milestone", "heat adaptation block", null);

        var result = CoachEpisodeSelector.Select([older, newer], WeekEnding, query, l2TopK: 1);

        result.Retrieved.Should().ContainSingle(e => e.Id == "ep_new");
    }

    private static CoachEpisodeQueryContext EmptyQuery() =>
        new(null, null, null, [], []);

    private static CoachEpisode Ep(
        string id,
        DateOnly date,
        string category,
        string summary,
        DateOnly? sourceWeekEnding) =>
        new()
        {
            Id = id,
            Date = date,
            Category = category,
            Summary = summary,
            SourceWeekEnding = sourceWeekEnding,
            Source = CoachEpisodeSources.Coach,
            Retired = false,
            CreatedAt = DateTime.UtcNow
        };
}
