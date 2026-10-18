using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachWeekSpliceTests
{
    private static readonly DateOnly WeekStart = new(2026, 10, 12);
    private static readonly DateOnly LockedThrough = new(2026, 10, 13); // Mon+Tue

    [Fact]
    public void SpliceLockedDays_PreservesElapsed_AllowsRemainingChanges()
    {
        var sourceError = CoachWeeklyCommitSchema.TryParseAndValidate(
            CoachWeeklyCommitSchemaTests.BuildCommitJson(distanceMForIndex: i => 8000 + i),
            WeekStart,
            out var source);
        sourceError.Should().BeNull();

        var proposedError = CoachWeeklyCommitSchema.TryParseAndValidate(
            CoachWeeklyCommitSchemaTests.BuildCommitJson(
                notes: "Replanned remaining days.",
                distanceMForIndex: _ => 9999),
            WeekStart,
            out var proposed);
        proposedError.Should().BeNull();

        var spliced = CoachWeekSplice.SpliceLockedDays(source!, proposed!, LockedThrough);
        spliced.CoachesNotes.Should().Contain("Replanned");
        spliced.Days[0].Run!.DistanceM.Should().Be(8000);
        spliced.Days[1].DayType.Should().Be("rest");
        spliced.Days[2].Run!.DistanceM.Should().Be(9999);

        CoachWeekSplice.ValidateLockedDaysUnchanged(source!, spliced, LockedThrough).Should().BeNull();
    }

    [Fact]
    public void ValidateLockedDaysUnchanged_RejectsMutatedElapsedDay()
    {
        var sourceError = CoachWeeklyCommitSchema.TryParseAndValidate(
            CoachWeeklyCommitSchemaTests.BuildCommitJson(distanceMForIndex: i => 8000),
            WeekStart,
            out var source);
        sourceError.Should().BeNull();

        var mutatedError = CoachWeeklyCommitSchema.TryParseAndValidate(
            CoachWeeklyCommitSchemaTests.BuildCommitJson(distanceMForIndex: _ => 1111),
            WeekStart,
            out var mutated);
        mutatedError.Should().BeNull();

        var error = CoachWeekSplice.ValidateLockedDaysUnchanged(source!, mutated!, LockedThrough);
        error.Should().Contain("2026-10-12");
    }

    [Fact]
    public void SpliceLockedDays_CarriesTypedMemoryChannelsFromProposed()
    {
        var sourceError = CoachWeeklyCommitSchema.TryParseAndValidate(
            CoachWeeklyCommitSchemaTests.BuildCommitJson(),
            WeekStart,
            out var source);
        sourceError.Should().BeNull();

        var proposedError = CoachWeeklyCommitSchema.TryParseAndValidate(
            CoachWeeklyCommitSchemaTests.BuildCommitJson(
                notes: "Adjusted remaining week",
                layer2EpisodeDrafts:
                [
                    new
                    {
                        date = "2026-10-14",
                        category = "issue",
                        summary = "Calf niggle onset mid-week"
                    }
                ]),
            WeekStart,
            out var proposed);
        proposedError.Should().BeNull();

        var spliced = CoachWeekSplice.SpliceLockedDays(source!, proposed!, LockedThrough);
        spliced.Layer2EpisodeDrafts.Should().HaveCount(1);
        spliced.Layer2EpisodeDrafts[0].Summary.Should().Contain("Calf niggle");
        spliced.Layer1Patches.Should().BeEmpty();
        spliced.Layer3RuleProposals.Should().BeEmpty();
    }
}
