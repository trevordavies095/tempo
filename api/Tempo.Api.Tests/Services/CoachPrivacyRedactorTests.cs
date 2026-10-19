using System.Text.Json;
using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachPrivacyRedactorTests
{
    [Fact]
    public void TryRedact_StripsNameEmailAndRouteCoordinates()
    {
        var packet = new
        {
            meta = new { weekStartDate = "2026-10-12" },
            athlete = new { name = "Ada", email = "ada@example.com", username = "ada" },
            checkIn = new
            {
                runs = new[]
                {
                    new
                    {
                        distanceM = 5000,
                        name = "Morning jog",
                        route = new
                        {
                            coordinates = new[] { new[] { -71.1, 42.3 }, new[] { -71.11, 42.31 } }
                        }
                    }
                }
            }
        };

        var ok = CoachPrivacyRedactor.TryRedact(packet, out var json, out var report, out var error);

        ok.Should().BeTrue(error);
        error.Should().BeNull();
        json.Should().NotContain("\"name\":");
        json.Should().NotContain("Ada");
        json.Should().NotContain("ada@example.com");
        json.Should().NotContain("coordinates");
        json.Should().NotContain("42.3");
        report.Stripped.Should().Contain(s => s.Category == "athleteIdentity");
        report.Stripped.Should().Contain(s => s.Category == "gpsRoutes");
        report.Included.Should().NotBeEmpty();
    }

    [Fact]
    public void TryRedact_FailsClosed_WhenResidualGpsRemainsInNotes()
    {
        var packet = new
        {
            checkIn = new
            {
                notes = "Started near [42.3601, -71.0589]"
            }
        };

        var ok = CoachPrivacyRedactor.TryRedact(packet, out var json, out _, out var error);

        ok.Should().BeFalse();
        json.Should().BeEmpty();
        error.Should().Contain("residual GPS");
    }

    [Fact]
    public void TryRedact_IncludesImperialPacesDisplay_FromSecPerKm()
    {
        var paces = new CoachLayer1Paces(402, 340, 309, 294);
        var packet = new CoachContextPacket(
            Meta: new CoachPacketMeta(
                "2026-10-19",
                "2026-10-25",
                "2026-10-12",
                "2026-10-18",
                "2026-10-19",
                "2026-10-25",
                "2026-10-13",
                -240,
                "imperial",
                DateTime.UtcNow),
            Goal: null,
            Constraints: [],
            Layer1: new CoachPacketLayer1(
                new CoachLayer1HrZones("AgeBased", 30, null, 190, []),
                paces,
                [],
                null,
                new CoachLayer1RaceStatus("race", "Half-Marathon", 21097.5, null, null, null),
                RunDaysPerWeek: 4,
                TrainingPhase: null,
                PacesDisplay: CoachPaceFormat.FromLayer1Paces(paces, "imperial")),
            Layer2: new CoachPacketLayer2([], [], new CoachPacketLayer2Selection(8, "2026-10-11")),
            Layer3: new CoachPacketLayer3([]),
            CheckIn: new CoachPacketCheckIn("2026-10-12", "2026-10-18", null, null, null, null, null, []),
            RecentTraining: new CoachPacketRecentTraining(
            [
                new CoachPacketRecentTrainingWeek(
                    "2026-10-12",
                    "2026-10-18",
                    1,
                    5000,
                    1800,
                    0,
                    0,
                    null,
                    [
                        new CoachPacketRecentTrainingRun(
                            Guid.NewGuid(),
                            new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc),
                            5000,
                            1800,
                            140,
                            160,
                            null,
                            null,
                            null,
                            AvgPaceDisplay: CoachPaceFormat.FormatFromDistanceAndDuration(
                                5000,
                                1800,
                                "imperial"))
                    ])
            ]));

        var ok = CoachPrivacyRedactor.TryRedact(packet, out var json, out _, out var error);

        ok.Should().BeTrue(error);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("meta").GetProperty("unitPreference").GetString().Should().Be("imperial");
        var display = root.GetProperty("layer1").GetProperty("pacesDisplay");
        display.GetProperty("unit").GetString().Should().Be("/mi");
        display.GetProperty("easy").GetString().Should().Be("10:47 /mi");
        display.GetProperty("tempo").GetString().Should().Be("9:07 /mi");
        root.GetProperty("layer1").GetProperty("paces").GetProperty("easySecPerKm").GetInt32()
            .Should().Be(402);
        var run = root.GetProperty("recentTraining").GetProperty("weeks")[0].GetProperty("runs")[0];
        run.GetProperty("avgPaceDisplay").GetString().Should().Be("9:39 /mi");
    }

    [Fact]
    public void TryRedact_ScrubsUsernameAndEmailInMemoryText()
    {
        var packet = new CoachContextPacket(
            Meta: new CoachPacketMeta(
                "2026-10-19",
                "2026-10-25",
                "2026-10-12",
                "2026-10-18",
                "2026-10-19",
                "2026-10-25",
                "2026-10-13",
                0,
                "metric",
                DateTime.UtcNow),
            Goal: null,
            Constraints: [],
            Layer1: new CoachPacketLayer1(
                new CoachLayer1HrZones("AgeBased", 30, null, 190, []),
                new CoachLayer1Paces(360, 300, 280, 250),
                [],
                null,
                new CoachLayer1RaceStatus("race", "half_marathon", 21097, null, null, null)),
            Layer2: new CoachPacketLayer2(
                PriorWeek:
                [
                    new CoachPacketEpisode(
                        "ep_1",
                        "2026-10-10",
                        "issue",
                        "testuser felt calf after long — email me at leak@example.com",
                        "2026-10-11")
                ],
                Retrieved: [],
                Selection: new CoachPacketLayer2Selection(8, "2026-10-11"),
                TotalEpisodeCount: 1),
            Layer3: new CoachPacketLayer3(
            [
                new CoachPacketRule(
                    "rule_1",
                    "key_learnings",
                    "Never schedule quality the day after travel for testuser",
                    ["obs"])
            ]),
            CheckIn: new CoachPacketCheckIn("2026-10-12", "2026-10-18", null, null, null, null, null, []),
            RecentTraining: new CoachPacketRecentTraining(
            [
                new CoachPacketRecentTrainingWeek(
                    "2026-10-12",
                    "2026-10-18",
                    1,
                    5000,
                    1800,
                    0,
                    0,
                    null,
                    [
                        new CoachPacketRecentTrainingRun(
                            Guid.NewGuid(),
                            new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc),
                            5000,
                            1800,
                            140,
                            160,
                            null,
                            null,
                            "testuser easy day — leak@example.com")
                    ])
            ]));

        var ok = CoachPrivacyRedactor.TryRedact(
            packet,
            out var json,
            out var report,
            out var error,
            new CoachRedactionContext("testuser"));

        ok.Should().BeTrue(error);
        json.Should().NotContain("testuser");
        json.Should().NotContain("leak@example.com");
        json.Should().Contain("[redacted]");
        report.SelectedEpisodeIds.Should().Contain("ep_1");
        report.SelectedRuleIds.Should().Contain("rule_1");
        report.Included.Should().Contain(i =>
            i.Category == "layer2" && i.Detail.Contains("1 episodes selected of 1"));
        report.Included.Should().Contain(i =>
            i.Category == "layer3" && i.Detail.Contains("1 active standing rules"));
        report.Included.Should().Contain(i =>
            i.Category == "recentTraining" && i.Detail.Contains("1 Mon–Sun weeks") && i.Detail.Contains("1 runs"));
        report.Stripped.Should().Contain(s => s.Category == "hrStreams");
        report.Stripped.Should().Contain(s => s.Category == "similarRoutes");
    }

    [Fact]
    public void TryRedact_FailsClosed_WhenGpsInEpisodeSummary()
    {
        var packet = new CoachContextPacket(
            Meta: new CoachPacketMeta(
                "2026-10-19",
                "2026-10-25",
                "2026-10-12",
                "2026-10-18",
                "2026-10-19",
                "2026-10-25",
                "2026-10-13",
                0,
                "metric",
                DateTime.UtcNow),
            Goal: null,
            Constraints: [],
            Layer1: new CoachPacketLayer1(
                new CoachLayer1HrZones("AgeBased", 30, null, 190, []),
                new CoachLayer1Paces(0, 0, 0, 0),
                [],
                null,
                new CoachLayer1RaceStatus("none", "", 0, null, null, null)),
            Layer2: new CoachPacketLayer2(
                PriorWeek:
                [
                    new CoachPacketEpisode(
                        "ep_gps",
                        "2026-10-10",
                        "milestone",
                        "Started near [42.3601, -71.0589]",
                        null)
                ],
                Retrieved: [],
                Selection: new CoachPacketLayer2Selection(8, "2026-10-11"),
                TotalEpisodeCount: 1),
            Layer3: new CoachPacketLayer3([]),
            CheckIn: new CoachPacketCheckIn("2026-10-12", "2026-10-18", null, null, null, null, null, []),
            RecentTraining: new CoachPacketRecentTraining([]));

        var ok = CoachPrivacyRedactor.TryRedact(packet, out var json, out _, out var error);

        ok.Should().BeFalse();
        json.Should().BeEmpty();
        error.Should().Contain("residual GPS");
    }

    [Fact]
    public void TryRedact_FailsClosed_WhenGpsInRecentTrainingNotes()
    {
        var packet = new CoachContextPacket(
            Meta: new CoachPacketMeta(
                "2026-10-19",
                "2026-10-25",
                "2026-10-12",
                "2026-10-18",
                "2026-10-19",
                "2026-10-25",
                "2026-10-13",
                0,
                "metric",
                DateTime.UtcNow),
            Goal: null,
            Constraints: [],
            Layer1: new CoachPacketLayer1(
                new CoachLayer1HrZones("AgeBased", 30, null, 190, []),
                new CoachLayer1Paces(0, 0, 0, 0),
                [],
                null,
                new CoachLayer1RaceStatus("none", "", 0, null, null, null)),
            Layer2: new CoachPacketLayer2([], [], new CoachPacketLayer2Selection(8, "2026-10-11")),
            Layer3: new CoachPacketLayer3([]),
            CheckIn: new CoachPacketCheckIn("2026-10-12", "2026-10-18", null, null, null, null, null, []),
            RecentTraining: new CoachPacketRecentTraining(
            [
                new CoachPacketRecentTrainingWeek(
                    "2026-10-12",
                    "2026-10-18",
                    1,
                    5000,
                    1800,
                    0,
                    0,
                    null,
                    [
                        new CoachPacketRecentTrainingRun(
                            Guid.NewGuid(),
                            new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc),
                            5000,
                            1800,
                            null,
                            null,
                            null,
                            null,
                            "Started near [42.3601, -71.0589]")
                    ])
            ]));

        var ok = CoachPrivacyRedactor.TryRedact(packet, out var json, out _, out var error);

        ok.Should().BeFalse();
        json.Should().BeEmpty();
        error.Should().Contain("residual GPS");
    }

    [Fact]
    public void ExtractRecentTrainingSummary_FromRedactedPayload()
    {
        const string json = """
            {
              "recentTraining": {
                "weeks": [
                  {
                    "weekStartDate": "2026-09-21",
                    "weekEndDate": "2026-09-27",
                    "runCount": 0,
                    "runs": []
                  },
                  {
                    "weekStartDate": "2026-10-12",
                    "weekEndDate": "2026-10-18",
                    "runCount": 1,
                    "runs": [{ "workoutId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }]
                  }
                ]
              }
            }
            """;

        var weeks = CoachPrivacyRedactor.ExtractRecentTrainingSummary(json);
        weeks.Should().HaveCount(2);
        weeks[0].RunCount.Should().Be(0);
        weeks[1].RunCount.Should().Be(1);
        weeks[1].WorkoutIds.Should().Contain("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    }

    [Fact]
    public void LooksLikeResidualGps_DetectsLatProperty()
    {
        CoachPrivacyRedactor.LooksLikeResidualGps("{\"lat\":42.3}").Should().BeTrue();
        CoachPrivacyRedactor.LooksLikeResidualGps("{\"distanceM\":5000}").Should().BeFalse();
    }

    [Fact]
    public void ExtractSelectedIds_FromRedactedPayload()
    {
        const string json = """
            {
              "layer2": {
                "priorWeek": [{ "id": "ep_a" }],
                "retrieved": [{ "id": "ep_b" }],
                "selection": { "l2TopK": 8, "priorWeekEnding": "2026-10-11" }
              },
              "layer3": {
                "rules": [{ "id": "rule_x" }]
              }
            }
            """;

        var (episodes, rules) = CoachPrivacyRedactor.ExtractSelectedIds(json);
        episodes.Should().BeEquivalentTo(["ep_a", "ep_b"]);
        rules.Should().BeEquivalentTo(["rule_x"]);
    }

    [Fact]
    public void TryRedact_CleanPacket_Succeeds()
    {
        var packet = new
        {
            meta = new { weekStartDate = "2026-10-12", unitPreference = "metric" },
            checkIn = new
            {
                runs = new[]
                {
                    new { workoutId = Guid.NewGuid(), distanceM = 10000.0, durationS = 3000, notes = "felt fine" }
                }
            }
        };

        var ok = CoachPrivacyRedactor.TryRedact(packet, out var json, out var report, out var error);

        ok.Should().BeTrue(error);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("checkIn").GetProperty("runs")[0].GetProperty("distanceM").GetDouble()
            .Should().Be(10000);
        report.Included.Should().Contain(i => i.Category == "checkIn");
    }

    [Fact]
    public void TryRedact_ScrubsEmailInQuestionForCoach()
    {
        var packet = new CoachPacketCheckIn(
            "2026-10-12",
            "2026-10-18",
            "felt ok",
            4,
            null,
            null,
            "email me at athlete@example.com about long run?",
            []);

        var ok = CoachPrivacyRedactor.TryRedact(
            new { checkIn = packet },
            out var json,
            out _,
            out var error,
            new CoachRedactionContext(Username: null, Email: null));

        ok.Should().BeTrue(error);
        json.Should().Contain("[redacted]");
        json.Should().NotContain("athlete@example.com");
        json.Should().Contain("\"feelScore\":4");
    }
}
