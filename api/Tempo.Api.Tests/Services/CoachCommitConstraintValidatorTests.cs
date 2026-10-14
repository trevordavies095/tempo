using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachCommitConstraintValidatorTests
{
    private static readonly DateOnly WeekStart = new(2026, 10, 12);

    [Fact]
    public void Blackout_FailsWhenNonRestScheduled()
    {
        var json = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            dayTypeForIndex: i => i == 2 ? "easy" : (i == 6 ? "long" : "rest"));
        CoachWeeklyCommitSchema.TryParseAndValidate(json, WeekStart, out var commit).Should().BeNull();

        var constraints = new[]
        {
            new CoachConstraint
            {
                Kind = CoachConstraintKinds.Blackout,
                StartDate = new DateOnly(2026, 10, 14),
                EndDate = new DateOnly(2026, 10, 14)
            }
        };

        CoachCommitConstraintValidator.Validate(commit!, constraints)
            .Should().Contain("Blackout");
    }

    [Fact]
    public void NoRunOn_FailsWhenRunScheduled()
    {
        var json = CoachWeeklyCommitSchemaTests.BuildCommitJson();
        CoachWeeklyCommitSchema.TryParseAndValidate(json, WeekStart, out var commit).Should().BeNull();

        // Wednesday = ISO 3 (2026-10-14) is easy in default builder
        var constraints = new[]
        {
            new CoachConstraint
            {
                Kind = CoachConstraintKinds.StandingSchedule,
                Preset = CoachStandingPresets.NoRunOn,
                Weekdays = [3]
            }
        };

        CoachCommitConstraintValidator.Validate(commit!, constraints)
            .Should().Contain("no_run_on");
    }

    [Fact]
    public void SecondaryRace_RequiresRaceType()
    {
        var json = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            dayTypeForIndex: _ => "easy",
            runTypeForIndex: _ => "easy");
        CoachWeeklyCommitSchema.TryParseAndValidate(json, WeekStart, out var commit).Should().BeNull();

        var constraints = new[]
        {
            new CoachConstraint
            {
                Kind = CoachConstraintKinds.SecondaryRace,
                StartDate = new DateOnly(2026, 10, 15),
                Distance = "5K",
                Intent = CoachConstraintIntents.TuneUp
            }
        };

        CoachCommitConstraintValidator.Validate(commit!, constraints)
            .Should().Contain("secondary_race");
    }

    [Fact]
    public void SecondaryRace_PassesWhenRaceType()
    {
        var json = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            dayTypeForIndex: i => i == 3 ? "race" : (i == 6 ? "long" : "easy"),
            runTypeForIndex: i => i == 3 ? "race" : (i == 6 ? "long" : "easy"));
        CoachWeeklyCommitSchema.TryParseAndValidate(json, WeekStart, out var commit).Should().BeNull();

        var constraints = new[]
        {
            new CoachConstraint
            {
                Kind = CoachConstraintKinds.SecondaryRace,
                StartDate = new DateOnly(2026, 10, 15),
                Distance = "5K",
                Intent = CoachConstraintIntents.TuneUp
            }
        };

        CoachCommitConstraintValidator.Validate(commit!, constraints).Should().BeNull();
    }
}
