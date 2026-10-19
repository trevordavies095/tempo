using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachMemoryProposalValidatorTests
{
    private static readonly DateOnly WeekStart = new(2026, 10, 12);

    [Fact]
    public void Stamp_MarksUnknownL1PathInvalid_WithoutFailingWeek()
    {
        var commit = Parse(CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer1Patches:
            [
                new
                {
                    fieldPath = "shoes.defaultShoeId",
                    oldValue = (object?)null,
                    newValue = Guid.NewGuid().ToString(),
                    rationale = "Nope"
                }
            ]));

        CoachMemoryProposalValidator.Stamp(
            commit,
            WeekStart,
            new HashSet<string>(),
            new Dictionary<string, CoachRule>());

        commit.Layer1Patches[0].Validity!.Ok.Should().BeFalse();
        commit.Layer1Patches[0].Validity!.Reason.Should().Contain("allowlist");
    }

    [Fact]
    public void Stamp_MarksEpisodeCollisionInvalid()
    {
        var summary = "Travel blackout week noted";
        var date = WeekStart;
        var prospective = CoachMemoryIds.ProspectiveEpisodeId(date, "milestone", summary);
        var commit = Parse(CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer2EpisodeDrafts:
            [
                new
                {
                    date = date.ToString("yyyy-MM-dd"),
                    category = "milestone",
                    summary
                }
            ]));

        CoachMemoryProposalValidator.Stamp(
            commit,
            WeekStart,
            new HashSet<string> { prospective },
            new Dictionary<string, CoachRule>(),
            asOfDate: new DateOnly(2026, 10, 18));

        commit.Layer2EpisodeDrafts[0].Validity!.Ok.Should().BeFalse();
        commit.Layer2EpisodeDrafts[0].Validity!.Reason.Should().Contain("already in memory");
    }

    [Fact]
    public void Stamp_MarksReviseWithoutActiveTargetInvalid()
    {
        var commit = Parse(CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer3RuleProposals:
            [
                new
                {
                    operation = "revise",
                    ruleText = "Updated rule",
                    evidence = new[] { "obs" },
                    targetRuleId = "rule_missing",
                    targetLayer = "key_learnings"
                }
            ]));

        CoachMemoryProposalValidator.Stamp(
            commit,
            WeekStart,
            new HashSet<string>(),
            new Dictionary<string, CoachRule>());

        commit.Layer3RuleProposals[0].Validity!.Ok.Should().BeFalse();
        commit.Layer3RuleProposals[0].Validity!.Reason.Should().Contain("active rule");
    }

    [Fact]
    public void Stamp_AcceptsAllowlistedPacePatch()
    {
        var commit = Parse(CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer1Patches:
            [
                new
                {
                    fieldPath = "paces.easySecPerKm",
                    oldValue = 360,
                    newValue = 350,
                    rationale = "Settled easier"
                }
            ]));

        CoachMemoryProposalValidator.Stamp(
            commit,
            WeekStart,
            new HashSet<string>(),
            new Dictionary<string, CoachRule>());

        commit.Layer1Patches[0].Validity!.Ok.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(4, true)]
    [InlineData(0, false)]
    [InlineData(8, false)]
    public void Stamp_RunDaysPerWeek_RequiresIntegerOneToSeven(int newValue, bool ok)
    {
        var commit = Parse(CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer1Patches:
            [
                new
                {
                    fieldPath = "runDaysPerWeek",
                    oldValue = 3,
                    newValue,
                    rationale = "Settled frequency"
                }
            ]));

        CoachMemoryProposalValidator.Stamp(
            commit,
            WeekStart,
            new HashSet<string>(),
            new Dictionary<string, CoachRule>());

        commit.Layer1Patches[0].Validity!.Ok.Should().Be(ok);
        if (!ok)
        {
            commit.Layer1Patches[0].Validity!.Reason.Should().Contain("1 to 7");
        }
    }

    [Fact]
    public void Stamp_RunDaysPerWeek_RejectsString()
    {
        var commit = Parse(CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer1Patches:
            [
                new
                {
                    fieldPath = "runDaysPerWeek",
                    oldValue = 3,
                    newValue = "four",
                    rationale = "Nope"
                }
            ]));

        CoachMemoryProposalValidator.Stamp(
            commit,
            WeekStart,
            new HashSet<string>(),
            new Dictionary<string, CoachRule>());

        commit.Layer1Patches[0].Validity!.Ok.Should().BeFalse();
        commit.Layer1Patches[0].Validity!.Reason.Should().Contain("1 to 7");
    }

    [Fact]
    public void Stamp_Revise_StampsCurrentRuleText_FromActiveRule()
    {
        var active = new CoachRule
        {
            Id = "rule_heat_mp",
            TargetLayer = CoachRuleTargetLayers.DataInterpretationNotes,
            RuleText = "Afternoon MP costs ~2x recovery in heat",
            Status = CoachRuleStatuses.Active,
            Evidence = ["Aug 11 PM"],
            Source = CoachRuleSources.Coach
        };
        var commit = Parse(CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer3RuleProposals:
            [
                new
                {
                    operation = "revise",
                    ruleText = "Afternoon MP costs ~2x recovery when solar load is high",
                    evidence = new[] { "Aug 11 PM", "Aug 15 AM" },
                    targetRuleId = active.Id,
                    targetLayer = "data_interpretation_notes"
                }
            ]));

        CoachMemoryProposalValidator.Stamp(
            commit,
            WeekStart,
            new HashSet<string>(),
            new Dictionary<string, CoachRule> { [active.Id] = active });

        var proposal = commit.Layer3RuleProposals[0];
        proposal.Validity!.Ok.Should().BeTrue();
        proposal.CurrentRuleText.Should().Be(active.RuleText);
    }

    [Fact]
    public void Stamp_Promote_ClearsCurrentRuleText()
    {
        var commit = Parse(CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer3RuleProposals:
            [
                new
                {
                    operation = "promote",
                    ruleText = "New standing rule",
                    evidence = new[] { "obs" },
                    targetRuleId = (string?)null,
                    targetLayer = "key_learnings"
                }
            ]));
        commit.Layer3RuleProposals[0].CurrentRuleText = "stale";

        CoachMemoryProposalValidator.Stamp(
            commit,
            WeekStart,
            new HashSet<string>(),
            new Dictionary<string, CoachRule>());

        commit.Layer3RuleProposals[0].Validity!.Ok.Should().BeTrue();
        commit.Layer3RuleProposals[0].CurrentRuleText.Should().BeNull();
    }

    [Fact]
    public void Stamp_ReviseMissingTarget_ClearsCurrentRuleText()
    {
        var commit = Parse(CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer3RuleProposals:
            [
                new
                {
                    operation = "revise",
                    ruleText = "Updated",
                    evidence = new[] { "obs" },
                    targetRuleId = "rule_missing",
                    targetLayer = "key_learnings"
                }
            ]));
        commit.Layer3RuleProposals[0].CurrentRuleText = "stale";

        CoachMemoryProposalValidator.Stamp(
            commit,
            WeekStart,
            new HashSet<string>(),
            new Dictionary<string, CoachRule>());

        commit.Layer3RuleProposals[0].CurrentRuleText.Should().BeNull();
        commit.Layer3RuleProposals[0].Validity!.Ok.Should().BeFalse();
    }

    private static CoachWeeklyCommit Parse(string json)
    {
        var error = CoachWeeklyCommitSchema.TryParseAndValidate(json, WeekStart, out var commit);
        error.Should().BeNull();
        return commit!;
    }
}
