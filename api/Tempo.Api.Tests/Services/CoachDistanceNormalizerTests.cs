using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachDistanceNormalizerTests
{
    private static readonly DateOnly WeekStart = new(2026, 10, 12);

    [Theory]
    [InlineData(4800, 3.0)] // common "3 mi ≈ 4800 m" mistake
    [InlineData(6400, 4.0)]
    [InlineData(11300, 7.0)]
    [InlineData(4828.032, 3.0)] // already exact
    public void NormalizePlannedDistanceM_SnapsNearHalfMiles(double inputM, double expectedMi)
    {
        var normalized = CoachDistanceNormalizer.NormalizePlannedDistanceM(inputM);
        (normalized / CoachDistanceNormalizer.MetersPerMile).Should().BeApproximately(expectedMi, 1e-9);
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(8000)]
    [InlineData(10000)]
    [InlineData(16000)]
    public void NormalizePlannedDistanceM_PreservesRoundKilometers(double meters)
    {
        CoachDistanceNormalizer.NormalizePlannedDistanceM(meters).Should().Be(meters);
    }

    [Fact]
    public void StripRedundantLeadingDistance_RemovesMatchingTotal()
    {
        var distanceM = 3 * CoachDistanceNormalizer.MetersPerMile;
        var cleaned = CoachDistanceNormalizer.StripRedundantLeadingDistance(
            "3 mi very easy shakeout — truly conversational, no pace targets",
            distanceM);

        cleaned.Should().Be("very easy shakeout — truly conversational, no pace targets");
    }

    [Fact]
    public void StripRedundantLeadingDistance_KeepsIntervalRecipes()
    {
        var distanceM = 6 * CoachDistanceNormalizer.MetersPerMile;
        var structure = "2mi WU + 2mi @ tempo + 2mi CD";
        CoachDistanceNormalizer.StripRedundantLeadingDistance(structure, distanceM)
            .Should()
            .Be(structure);
    }

    [Fact]
    public void TryParseAndValidate_NormalizesApproximateMileMetersAndStructure()
    {
        const string json = """
            {
              "coachesNotes": "Keep easy days easy and protect the long.",
              "days": [
                { "date": "2026-10-12", "dayType": "rest", "run": null, "strength": null },
                { "date": "2026-10-13", "dayType": "easy", "run": { "type": "easy", "distanceM": 4800, "structure": "3 mi easy shakeout", "paceOrHr": "Z2", "shoeId": null, "notes": null }, "strength": null },
                { "date": "2026-10-14", "dayType": "easy", "run": { "type": "easy", "distanceM": 8000, "structure": null, "paceOrHr": null, "shoeId": null, "notes": null }, "strength": null },
                { "date": "2026-10-15", "dayType": "rest", "run": null, "strength": null },
                { "date": "2026-10-16", "dayType": "quality", "run": { "type": "quality", "distanceM": 8000, "structure": "2mi WU + 2mi @ tempo + 1mi CD", "paceOrHr": null, "shoeId": null, "notes": null }, "strength": null },
                { "date": "2026-10-17", "dayType": "rest", "run": null, "strength": null },
                { "date": "2026-10-18", "dayType": "long", "run": { "type": "long", "distanceM": 16000, "structure": null, "paceOrHr": null, "notes": null, "shoeId": null }, "strength": null }
              ],
              "safetyFlags": [],
              "layer1Patches": [],
              "layer2EpisodeDrafts": [],
              "layer3RuleProposals": [],
              "clarifyingQuestion": null,
              "callouts": []
            }
            """;

        var error = CoachWeeklyCommitSchema.TryParseAndValidate(json, WeekStart, out var commit);
        error.Should().BeNull();

        var easy = commit!.Days[1].Run!;
        easy.DistanceM.Should().BeApproximately(3 * CoachDistanceNormalizer.MetersPerMile, 1e-6);
        easy.Structure.Should().Be("easy shakeout");

        var quality = commit.Days[4].Run!;
        quality.Structure.Should().Be("2mi WU + 2mi @ tempo + 1mi CD");
        quality.DistanceM.Should().Be(8000);
    }
}
