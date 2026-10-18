using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
public class CoachAdjustEndpointsTests : IClassFixture<CoachGenerateTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachGenerateTestFactory _factory;

    public CoachAdjustEndpointsTests(CoachGenerateTestFactory factory)
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

    private static async Task SaveEmptyCheckInAsync(HttpClient client, string referenceDate)
    {
        var put = await client.PutAsJsonAsync("/coach/check-in", new
        {
            referenceDate,
            timezoneOffsetMinutes = 0,
            overallFeel = (string?)null,
            niggles = (string?)null,
            lifeConstraints = (string?)null,
            runNotes = Array.Empty<object>()
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Ritual assemble: referenceDate identifies check-in week N (requires saved check-in).
    /// Adjust assemble: pass <paramref name="forAdjust"/> true after StartAdjust — no check-in gate.
    /// </summary>
    private static async Task<Guid> ConsentPreflightAsync(
        HttpClient client,
        string referenceDate,
        bool forAdjust = false)
    {
        if (!forAdjust)
        {
            await SaveEmptyCheckInAsync(client, referenceDate);
        }

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

    private async Task<HttpClient> SeedCommittedWeekAsync(string commitJson)
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);
        // Check-in week 2026-10-05 → plan week 2026-10-12 (committed identity under test).
        var preflightId = await ConsentPreflightAsync(client, referenceDate: "2026-10-06");

        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true, commitJson, 10, 20, CoachProtocolBrainError.None, null));

        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId });
        generate.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await generate.Content.ReadFromJsonAsync<JobResponse>(JsonOptions);
        var job = await PollUntilTerminalAsync(client, started!.Id);
        job.Status.Should().Be("ready");

        var apply = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedLayer1PatchIndexes = Array.Empty<int>(),
            acceptedEpisodeIndexes = Array.Empty<int>(),
            acceptedRuleProposalIndexes = Array.Empty<int>(),
            archiveNotesAsRecap = false
        });
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    [Fact]
    public async Task LockedCommittedWeek_CoachReads_DoNotCallAnthropic()
    {
        var client = await SeedCommittedWeekAsync(CoachWeeklyCommitSchemaTests.BuildCommitJson());
        var callsBefore = _factory.Brain.CallCount;

        (await client.GetAsync("/coach")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/coach/weeks/committed?weekStartDate=2026-10-12"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/coach/check-in?timezoneOffsetMinutes=0&referenceDate=2026-10-15"))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        _factory.Brain.CallCount.Should().Be(callsBefore);
    }

    [Fact]
    public async Task StartAdjust_MissingReason_ReturnsBadRequest()
    {
        var client = await SeedCommittedWeekAsync(CoachWeeklyCommitSchemaTests.BuildCommitJson());

        var response = await client.PostAsJsonAsync("/coach/weeks/committed/adjust", new
        {
            weekStartDate = "2026-10-12",
            reason = "   ",
            category = "niggle",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-15"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Brain.CallCount.Should().Be(1); // only the original generate
    }

    [Fact]
    public async Task Adjust_RemainingDaysSplice_ArchivesPriorCommitted()
    {
        var originalJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            notes: "Original committed week notes.",
            distanceMForIndex: i => 8000 + i);
        var client = await SeedCommittedWeekAsync(originalJson);
        var callsAfterCommit = _factory.Brain.CallCount;

        Guid priorCommittedId;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            priorCommittedId = await db.CoachWeeks
                .Where(w => w.Status == CoachWeekStatuses.Committed)
                .Select(w => w.Id)
                .SingleAsync();
        }

        // Mid-week (Wed 2026-10-14): Mon/Tue locked (through 2026-10-13).
        var start = await client.PostAsJsonAsync("/coach/weeks/committed/adjust", new
        {
            weekStartDate = "2026-10-12",
            reason = "Travel day moved quality session",
            category = "life",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-14"
        });
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var adjustment = await start.Content.ReadFromJsonAsync<AdjustResponse>(JsonOptions);
        adjustment!.LockedThroughDate.Should().Be("2026-10-13");
        _factory.Brain.CallCount.Should().Be(callsAfterCommit);

        // Adjust path: no new check-in required.
        var preflightId = await ConsentPreflightAsync(client, referenceDate: "2026-10-14", forAdjust: true);

        // Fake brain mutates every day distance — worker must splice locked Mon/Tue back.
        var mutated = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            notes: "Adjusted remaining week after travel.",
            distanceMForIndex: _ => 9999);
        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true, mutated, 5, 5, CoachProtocolBrainError.None, null));

        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId });
        generate.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await generate.Content.ReadFromJsonAsync<JobResponse>(JsonOptions);
        started!.Kind.Should().Be("adjust");
        var job = await PollUntilTerminalAsync(client, started.Id);
        job.Status.Should().Be("ready");
        _factory.Brain.CallCount.Should().Be(callsAfterCommit + 1);

        var draftRes = await client.GetAsync("/coach/weeks/draft?weekStartDate=2026-10-12");
        draftRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using var draftDoc = JsonDocument.Parse(await draftRes.Content.ReadAsStringAsync());
        draftDoc.RootElement.GetProperty("isAdjustment").GetBoolean().Should().BeTrue();
        draftDoc.RootElement.GetProperty("lockedThroughDate").GetString().Should().Be("2026-10-13");

        var days = draftDoc.RootElement.GetProperty("commit").GetProperty("days");
        days[0].GetProperty("run")!.GetProperty("distanceM").GetDouble().Should().Be(8000); // Mon locked
        days[1].GetProperty("dayType").GetString().Should().Be("rest"); // Tue rest locked
        days[2].GetProperty("run")!.GetProperty("distanceM").GetDouble().Should().Be(9999); // Wed remaining

        var apply = await client.PostAsJsonAsync("/coach/weeks/draft/apply", new
        {
            weekStartDate = "2026-10-12",
            safetyAcknowledgments = Array.Empty<object>(),
            acceptedLayer1PatchIndexes = Array.Empty<int>(),
            acceptedEpisodeIndexes = Array.Empty<int>(),
            acceptedRuleProposalIndexes = Array.Empty<int>(),
            archiveNotesAsRecap = false
        });
        apply.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var archived = await db.CoachWeeks.SingleAsync(w => w.Id == priorCommittedId);
            archived.Status.Should().Be(CoachWeekStatuses.Archived);

            var committed = await db.CoachWeeks.SingleAsync(w => w.Status == CoachWeekStatuses.Committed);
            committed.Id.Should().NotBe(priorCommittedId);
            committed.CommitJson.Should().Contain("Adjusted remaining week");
            committed.CommitJson.Should().Contain("8000");
            committed.CommitJson.Should().Contain("9999");

            var adj = await db.CoachAdjustments.SingleAsync(a => a.Id == adjustment.Id);
            adj.Status.Should().Be(CoachAdjustmentStatuses.Applied);
        }

        var committedHttp = await client.GetAsync("/coach/weeks/committed?weekStartDate=2026-10-12");
        committedHttp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var committedDoc = JsonDocument.Parse(await committedHttp.Content.ReadAsStringAsync());
        committedDoc.RootElement.GetProperty("isAdjustment").GetBoolean().Should().BeTrue();

        var receipt = await client.GetAsync(
            $"/coach/weeks/{committedDoc.RootElement.GetProperty("id").GetGuid()}/privacy-receipt");
        receipt.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PlainRegenerate_StillConflicts_WithoutStartedAdjust()
    {
        var client = await SeedCommittedWeekAsync(CoachWeeklyCommitSchemaTests.BuildCommitJson());
        // Same check-in week → same plan week (already committed) → 409 without Adjust.
        var preflightId = await ConsentPreflightAsync(client, referenceDate: "2026-10-06");
        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId });
        generate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private sealed class PreflightResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class JobResponse
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public string Kind { get; set; } = "";
        public Guid? ResultWeekId { get; set; }
    }

    private sealed class AdjustResponse
    {
        public Guid Id { get; set; }
        public string LockedThroughDate { get; set; } = "";
    }
}
