using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Tempo.Api.Tests.Services;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class CoachReviewEndpointsTests : IClassFixture<CoachGenerateTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachGenerateTestFactory _factory;

    public CoachReviewEndpointsTests(CoachGenerateTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        _factory.Brain.Reset();
        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "testuser");
        if (user == null)
        {
            user = await TestDataSeeder.SeedUserAsync(db, "testuser");
        }

        user.OnboardingCompleted = true;
        await db.SaveChangesAsync();

        return await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory.Decorated);
    }

    private static async Task OptInAndProviderAsync(HttpClient client)
    {
        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-test" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task SeedLayer1Async(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var doc = new CoachLayer1Document(
            HrZones: new CoachLayer1HrZones("manual", 40, 50, 180, [new CoachLayer1HrZone(1, 100, 120)]),
            Paces: new CoachLayer1Paces(360, 300, 280, 250),
            Shoes: [],
            DefaultShoeId: null,
            RaceStatus: new CoachLayer1RaceStatus("race", "half_marathon", 21097.5, "2027-01-09", null, null),
            CompletedAt: DateTime.UtcNow,
            TrainingPhase: null);
        db.CoachMemoryLayers.Add(new CoachMemoryLayer
        {
            Layer = "L1",
            Document = CoachLayer1Json.Serialize(doc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> ConsentPreflightAsync(HttpClient client, string referenceDate = "2026-10-13")
    {
        var assemble = await client.PostAsync(
            $"/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate={referenceDate}",
            null);
        assemble.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await assemble.Content.ReadFromJsonAsync<PreflightResponse>(JsonOptions);
        doc.Should().NotBeNull();

        var consent = await client.PostAsync($"/coach/privacy/preflight/{doc!.Id}/consent", null);
        consent.StatusCode.Should().Be(HttpStatusCode.OK);
        return doc.Id;
    }

    private static async Task<JobResponse> PollUntilTerminalAsync(HttpClient client, Guid jobId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/coach/jobs/{jobId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var job = await response.Content.ReadFromJsonAsync<JobResponse>(JsonOptions);
            job.Should().NotBeNull();
            if (job!.Status is "ready" or "failed")
            {
                return job;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Coach job {jobId} did not reach a terminal status");
    }

    private async Task<(HttpClient Client, JsonElement Commit)> GenerateDraftAsync(
        string? commitJson = null,
        bool seedLayer1 = false)
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);
        if (seedLayer1)
        {
            await SeedLayer1Async(_factory.Decorated.Services);
        }

        var preflightId = await ConsentPreflightAsync(client);

        commitJson ??= CoachWeeklyCommitSchemaTests.BuildCommitJson();
        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true, commitJson, 10, 20, CoachProtocolBrainError.None, null));

        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId });
        generate.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await generate.Content.ReadFromJsonAsync<JobResponse>(JsonOptions);
        var job = await PollUntilTerminalAsync(client, started!.Id);
        job.Status.Should().Be("ready");

        var week = await client.GetAsync("/coach/weeks/draft?weekStartDate=2026-10-12");
        week.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await week.Content.ReadAsStringAsync());
        return (client, doc.RootElement.GetProperty("commit").Clone());
    }

    [Fact]
    public async Task Apply_HappyPath_CommitsWeek_WithoutWorkouts()
    {
        var (client, commit) = await GenerateDraftAsync();

        var edited = JsonNode.Parse(commit.GetRawText())!.AsObject();
        edited["days"]![0]!["run"]!["distanceM"] = 9000;

        var put = await client.PutAsJsonAsync("/coach/weeks/draft", new
        {
            weekStartDate = "2026-10-12",
            commit = edited
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var apply = await client.PostAsJsonAsync(
            "/coach/weeks/draft/apply",
            CoachWeeklyCommitSchemaTests.EmptyApplyBody());
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        var applied = await apply.Content.ReadFromJsonAsync<WeekResponse>(JsonOptions);
        applied!.Status.Should().Be("committed");

        (await client.GetAsync("/coach/weeks/draft?weekStartDate=2026-10-12"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var committed = await client.GetAsync("/coach/weeks/committed?weekStartDate=2026-10-12");
        committed.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.Workouts.CountAsync()).Should().Be(0);
        var week = await db.CoachWeeks.SingleAsync(w => w.Status == CoachWeekStatuses.Committed);
        week.ApplyRecordJson.Should().NotBeNullOrWhiteSpace();
        week.CommitJson.Should().Contain("9000");
    }

    [Fact]
    public async Task Apply_UnackedSafety_ReturnsBadRequest()
    {
        var commitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            safetyFlags:
            [
                new
                {
                    code = "niggle",
                    message = "Back off volume",
                    severity = "red",
                    requiresResponse = true
                }
            ]);
        var (client, _) = await GenerateDraftAsync(commitJson);

        var apply = await client.PostAsJsonAsync(
            "/coach/weeks/draft/apply",
            CoachWeeklyCommitSchemaTests.EmptyApplyBody());
        apply.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var applyNoResponse = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = new[]
            {
                new { index = 0, acknowledged = true, response = (string?)null }
            },
            acceptedLayer1PatchIndexes = Array.Empty<int>(),
            acceptedEpisodeIndexes = Array.Empty<int>(),
            acceptedRuleProposalIndexes = Array.Empty<int>(),
            archiveNotesAsRecap = false
        });
        applyNoResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.GetAsync("/coach/weeks/draft?weekStartDate=2026-10-12"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Apply_MemoryDefaultReject_AndAcceptEpisode()
    {
        var commitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer2EpisodeDrafts:
            [
                new
                {
                    date = "2026-10-12",
                    category = "milestone",
                    summary = "Travel blackout week noted"
                }
            ]);
        var (client, _) = await GenerateDraftAsync(commitJson);

        var rejectApply = await client.PostAsJsonAsync(
            "/coach/weeks/draft/apply",
            CoachWeeklyCommitSchemaTests.EmptyApplyBody());
        rejectApply.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.CoachEpisodes.CountAsync()).Should().Be(0);
        }

        var client2 = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client2);
        var preflightId = await ConsentPreflightAsync(client2, referenceDate: "2026-10-20");
        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true,
            CoachWeeklyCommitSchemaTests.BuildCommitJson(
                notes: "Next week plan.",
                weekStart: new DateOnly(2026, 10, 19),
                layer2EpisodeDrafts:
                [
                    new
                    {
                        date = "2026-10-19",
                        category = "milestone",
                        summary = "Travel blackout week noted"
                    }
                ]),
            1,
            1,
            CoachProtocolBrainError.None,
            null));
        var generate = await client2.PostAsJsonAsync("/coach/generate", new { preflightId });
        var started = await generate.Content.ReadFromJsonAsync<JobResponse>(JsonOptions);
        var job = await PollUntilTerminalAsync(client2, started!.Id);
        job.Status.Should().Be("ready");

        var acceptApply = await client2.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-19",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedLayer1PatchIndexes = Array.Empty<int>(),
            acceptedEpisodeIndexes = new[] { 0 },
            acceptedRuleProposalIndexes = Array.Empty<int>(),
            archiveNotesAsRecap = false
        });
        acceptApply.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var episode = await db.CoachEpisodes.SingleAsync();
            episode.Summary.Should().Be("Travel blackout week noted");
            episode.Category.Should().Be("milestone");
            episode.Source.Should().Be(CoachEpisodeSources.Coach);
        }
    }

    [Fact]
    public async Task Apply_AcceptsL1Patch_PromoteRule_AndRecap()
    {
        var commitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer1Patches:
            [
                new
                {
                    fieldPath = "paces.easySecPerKm",
                    oldValue = 360,
                    newValue = 350,
                    rationale = "Settled easier"
                }
            ],
            layer3RuleProposals:
            [
                new
                {
                    operation = "promote",
                    ruleText = "Morning MP recovers cleaner than afternoon MP",
                    evidence = new[] { "Aug 15 AM MP clean" },
                    targetRuleId = (string?)null,
                    targetLayer = "data_interpretation_notes"
                }
            ]);
        var (client, _) = await GenerateDraftAsync(commitJson, seedLayer1: true);

        var apply = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedLayer1PatchIndexes = new[] { 0 },
            acceptedEpisodeIndexes = Array.Empty<int>(),
            acceptedRuleProposalIndexes = new[] { 0 },
            archiveNotesAsRecap = true
        });
        apply.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var l1 = await db.CoachMemoryLayers.SingleAsync(l => l.Layer == "L1");
        var doc = CoachLayer1Json.Deserialize(l1.Document)!;
        doc.Paces.EasySecPerKm.Should().Be(350);

        var rule = await db.CoachRules.SingleAsync();
        rule.RuleText.Should().Contain("Morning MP");
        rule.Status.Should().Be(CoachRuleStatuses.Active);

        var recap = await db.CoachEpisodes.SingleAsync(e => e.Category == CoachEpisodeCategories.Recap);
        recap.Summary.Should().Contain("Keep the long easy");
        recap.Date.Should().Be(new DateOnly(2026, 10, 18));

        var committed = await client.GetAsync("/coach/weeks/committed?weekStartDate=2026-10-12");
        committed.StatusCode.Should().Be(HttpStatusCode.OK);
        using var committedDoc = JsonDocument.Parse(await committed.Content.ReadAsStringAsync());
        var root = committedDoc.RootElement;
        root.TryGetProperty("applyRecord", out var applyRecord).Should().BeTrue();
        applyRecord.ValueKind.Should().Be(JsonValueKind.Object);
        applyRecord.GetProperty("archiveNotesAsRecap").GetBoolean().Should().BeTrue();
        applyRecord.GetProperty("acceptedLayer1PatchIndexes").EnumerateArray().Select(e => e.GetInt32())
            .Should().Equal(0);
        applyRecord.GetProperty("acceptedRuleProposalIndexes").EnumerateArray().Select(e => e.GetInt32())
            .Should().Equal(0);
        applyRecord.GetProperty("appliedLayer1Paths").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("paces.easySecPerKm");
        applyRecord.GetProperty("appliedRuleIds").GetArrayLength().Should().Be(1);
        applyRecord.GetProperty("recapEpisodeId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DraftGet_ApplyRecord_IsNull()
    {
        var (client, _) = await GenerateDraftAsync();

        var draft = await client.GetAsync("/coach/weeks/draft?weekStartDate=2026-10-12");
        draft.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await draft.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("applyRecord", out var applyRecord).Should().BeTrue();
        applyRecord.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task DraftGet_ReviseProposal_IncludesCurrentRuleText()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            db.CoachRules.Add(new CoachRule
            {
                Id = "rule_heat_mp",
                TargetLayer = CoachRuleTargetLayers.DataInterpretationNotes,
                RuleText = "Afternoon MP costs ~2x recovery in heat",
                Status = CoachRuleStatuses.Active,
                Evidence = ["Aug 11 PM"],
                Source = CoachRuleSources.Athlete,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var commitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer3RuleProposals:
            [
                new
                {
                    operation = "revise",
                    ruleText = "Afternoon MP costs ~2x recovery when solar load is high",
                    evidence = new[] { "Aug 11 PM", "Aug 15 AM" },
                    targetRuleId = "rule_heat_mp",
                    targetLayer = "data_interpretation_notes"
                }
            ]);

        var preflightId = await ConsentPreflightAsync(client);
        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true, commitJson, 10, 20, CoachProtocolBrainError.None, null));
        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId });
        generate.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await generate.Content.ReadFromJsonAsync<JobResponse>(JsonOptions);
        var job = await PollUntilTerminalAsync(client, started!.Id);
        job.Status.Should().Be("ready");

        var week = await client.GetAsync("/coach/weeks/draft?weekStartDate=2026-10-12");
        week.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await week.Content.ReadAsStringAsync());
        var proposal = doc.RootElement.GetProperty("commit").GetProperty("layer3RuleProposals")[0];
        proposal.GetProperty("validity").GetProperty("ok").GetBoolean().Should().BeTrue();
        proposal.GetProperty("currentRuleText").GetString()
            .Should().Be("Afternoon MP costs ~2x recovery in heat");
    }

    [Fact]
    public async Task Apply_InvalidProposalAccept_Returns409()
    {
        var commitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer1Patches:
            [
                new
                {
                    fieldPath = "shoes.defaultShoeId",
                    oldValue = (object?)null,
                    newValue = "x",
                    rationale = "Not allowlisted"
                }
            ]);
        var (client, commit) = await GenerateDraftAsync(commitJson);
        commit.GetProperty("layer1Patches")[0].GetProperty("validity").GetProperty("ok")
            .GetBoolean().Should().BeFalse();

        var apply = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedLayer1PatchIndexes = new[] { 0 },
            acceptedEpisodeIndexes = Array.Empty<int>(),
            acceptedRuleProposalIndexes = Array.Empty<int>(),
            archiveNotesAsRecap = false
        });
        apply.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task JobLand_StampsInvalidMemory_WithoutFailingWeek()
    {
        var commitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            layer3RuleProposals:
            [
                new
                {
                    operation = "retire",
                    ruleText = (string?)null,
                    evidence = Array.Empty<string>(),
                    targetRuleId = "rule_missing",
                    targetLayer = "key_learnings"
                }
            ]);
        var (client, commit) = await GenerateDraftAsync(commitJson);
        commit.GetProperty("layer3RuleProposals")[0].GetProperty("validity").GetProperty("ok")
            .GetBoolean().Should().BeFalse();

        (await client.GetAsync("/coach/weeks/draft?weekStartDate=2026-10-12"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CommittedWeek_IsImmutable()
    {
        var (client, commit) = await GenerateDraftAsync();

        (await client.PostAsJsonAsync(
            "/coach/weeks/draft/apply",
            CoachWeeklyCommitSchemaTests.EmptyApplyBody())).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PutAsJsonAsync("/coach/weeks/draft", new
        {
            weekStartDate = "2026-10-12",
            commit
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.PostAsJsonAsync(
            "/coach/weeks/draft/apply",
            CoachWeeklyCommitSchemaTests.EmptyApplyBody())).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var preflightId = await ConsentPreflightAsync(client);
        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true, CoachWeeklyCommitSchemaTests.BuildCommitJson(), 1, 1, CoachProtocolBrainError.None, null));
        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId });
        generate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Apply_WithSafetyAckAndResponse_Succeeds()
    {
        var commitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            safetyFlags:
            [
                new
                {
                    code = "refer_out",
                    message = "See a clinician for severe pain",
                    severity = "red",
                    requiresResponse = true
                }
            ]);
        var (client, _) = await GenerateDraftAsync(commitJson);

        var apply = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = new[]
            {
                new { index = 0, acknowledged = true, response = "Understood — will rest and seek care." }
            },
            acceptedLayer1PatchIndexes = Array.Empty<int>(),
            acceptedEpisodeIndexes = Array.Empty<int>(),
            acceptedRuleProposalIndexes = Array.Empty<int>(),
            archiveNotesAsRecap = false
        });
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetupReset_WipesEpisodesAndRules()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            db.CoachEpisodes.Add(new CoachEpisode
            {
                Id = "ep_2026-10-01_race_seed",
                Date = new DateOnly(2026, 10, 1),
                Category = "race",
                Summary = "Seed race",
                Source = CoachEpisodeSources.Athlete,
                CreatedAt = DateTime.UtcNow
            });
            db.CoachRules.Add(new CoachRule
            {
                Id = "rule_seed",
                TargetLayer = CoachRuleTargetLayers.KeyLearnings,
                RuleText = "Seed rule",
                Status = CoachRuleStatuses.Active,
                Evidence = ["seed"],
                Source = CoachRuleSources.Athlete,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        (await client.DeleteAsync("/settings/coach/setup")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.CoachEpisodes.CountAsync()).Should().Be(0);
            (await db.CoachRules.CountAsync()).Should().Be(0);
        }
    }

    private sealed class PreflightResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class JobResponse
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public Guid? ResultWeekId { get; set; }
    }

    private sealed class WeekResponse
    {
        public string Status { get; set; } = "";
    }
}
