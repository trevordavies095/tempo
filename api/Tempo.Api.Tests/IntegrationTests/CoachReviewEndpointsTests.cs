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
        string? commitJson = null)
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);
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

        // Edit Monday distance via PUT
        var edited = JsonNode.Parse(commit.GetRawText())!.AsObject();
        edited["days"]![0]!["run"]!["distanceM"] = 9000;

        var put = await client.PutAsJsonAsync("/coach/weeks/draft", new
        {
            weekStartDate = "2026-10-12",
            commit = edited
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var apply = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedMemoryProposalIndexes = Array.Empty<int>()
        });
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

        var apply = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedMemoryProposalIndexes = Array.Empty<int>()
        });
        apply.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var applyNoResponse = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = new[]
            {
                new { index = 0, acknowledged = true, response = (string?)null }
            },
            acceptedMemoryProposalIndexes = Array.Empty<int>()
        });
        applyNoResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.GetAsync("/coach/weeks/draft?weekStartDate=2026-10-12"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Apply_MemoryDefaultReject_AndAcceptAppends()
    {
        var commitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            memoryProposals:
            [
                new { layer = "L2", path = "episodes.travel", summary = "Travel blackout week noted" }
            ]);
        var (client, _) = await GenerateDraftAsync(commitJson);

        var rejectApply = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedMemoryProposalIndexes = Array.Empty<int>()
        });
        rejectApply.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.CoachMemoryLayers.CountAsync(l => l.Layer == "L2")).Should().Be(0);
        }

        // New week draft for a different Monday so we can test accept path.
        // Use next week: wipe committed for cleaner path — regenerate is 409 for same week.
        // Instead seed a fresh generate on a new week via new preflight.
        var client2 = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client2);
        var preflightId = await ConsentPreflightAsync(client2, referenceDate: "2026-10-20");
        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true,
            BuildCommitForWeek(new DateOnly(2026, 10, 19), withMemory: true),
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
            acceptedMemoryProposalIndexes = new[] { 0 }
        });
        acceptApply.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var layer = await db.CoachMemoryLayers.SingleAsync(l => l.Layer == "L2");
            layer.Document.Should().Contain("acceptedProposals");
            layer.Document.Should().Contain("Travel blackout week noted");
        }
    }

    [Fact]
    public async Task CommittedWeek_IsImmutable()
    {
        var (client, commit) = await GenerateDraftAsync();

        (await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedMemoryProposalIndexes = Array.Empty<int>()
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PutAsJsonAsync("/coach/weeks/draft", new
        {
            weekStartDate = "2026-10-12",
            commit
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedMemoryProposalIndexes = Array.Empty<int>()
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

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
            acceptedMemoryProposalIndexes = Array.Empty<int>()
        });
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string BuildCommitForWeek(DateOnly weekStart, bool withMemory)
    {
        var days = new List<object>();
        for (var i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i).ToString("yyyy-MM-dd");
            var dayType = i == 6 ? "long" : i is 1 or 4 ? "rest" : "easy";
            object? run = dayType == "rest"
                ? null
                : new
                {
                    type = dayType,
                    distanceM = dayType == "long" ? 16000 : 8000,
                    structure = (string?)null,
                    paceOrHr = (string?)null,
                    shoeId = (string?)null,
                    notes = (string?)null
                };
            days.Add(new { date, dayType, run, strength = (object?)null });
        }

        object[] memory = withMemory
            ?
            [
                new { layer = "L2", path = "episodes.travel", summary = "Travel blackout week noted" }
            ]
            : [];

        return JsonSerializer.Serialize(new
        {
            coachesNotes = "Next week plan.",
            days,
            safetyFlags = Array.Empty<object>(),
            memoryProposals = memory,
            clarifyingQuestion = (string?)null,
            callouts = Array.Empty<string>()
        });
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
