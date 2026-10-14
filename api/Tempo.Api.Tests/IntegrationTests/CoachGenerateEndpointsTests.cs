using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Tempo.Api.Tests.Services;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class CoachGenerateEndpointsTests : IClassFixture<CoachGenerateTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachGenerateTestFactory _factory;

    public CoachGenerateEndpointsTests(CoachGenerateTestFactory factory)
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

    private static async Task<JobResponse> PollUntilTerminalAsync(HttpClient client, Guid jobId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
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

    [Fact]
    public async Task Generate_WithoutConsent_ReturnsBadRequest()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        var assemble = await client.PostAsync(
            "/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate=2026-10-13",
            null);
        var doc = await assemble.Content.ReadFromJsonAsync<PreflightResponse>(JsonOptions);

        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId = doc!.Id });
        generate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Brain.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Generate_WithConsent_ReachesReadyDraft()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);
        var preflightId = await ConsentPreflightAsync(client);

        var commitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson();
        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true, commitJson, 10, 20, CoachProtocolBrainError.None, null));

        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId });
        generate.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await generate.Content.ReadFromJsonAsync<JobResponse>(JsonOptions);
        started.Should().NotBeNull();

        var job = await PollUntilTerminalAsync(client, started!.Id);
        job.Status.Should().Be("ready");
        job.ResultWeekId.Should().NotBeNull();
        job.InputTokens.Should().Be(10);
        job.OutputTokens.Should().Be(20);
        _factory.Brain.CallCount.Should().Be(1);

        var week = await client.GetAsync("/coach/weeks/draft?weekStartDate=2026-10-12");
        week.StatusCode.Should().Be(HttpStatusCode.OK);
        var weekDoc = await week.Content.ReadFromJsonAsync<WeekResponse>(JsonOptions);
        weekDoc!.PayloadHash.Should().NotBeNullOrEmpty();
        weekDoc.Status.Should().Be("draft");
    }

    [Fact]
    public async Task Generate_SchemaRepair_RetriesOnce()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);
        var preflightId = await ConsentPreflightAsync(client);

        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true, "{not-json", 1, 1, CoachProtocolBrainError.None, null));
        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true, CoachWeeklyCommitSchemaTests.BuildCommitJson(), 2, 3, CoachProtocolBrainError.None, null));

        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId });
        var started = await generate.Content.ReadFromJsonAsync<JobResponse>(JsonOptions);
        var job = await PollUntilTerminalAsync(client, started!.Id);

        job.Status.Should().Be("ready");
        job.SchemaRepairAttempted.Should().BeTrue();
        _factory.Brain.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Generate_ConstraintFailure_DoesNotSchemaRepair()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        // Blackout Wednesday — commit schedules easy that day.
        (await client.PostAsJsonAsync("/coach/constraints", new
        {
            kind = "blackout",
            startDate = "2026-10-14",
            endDate = "2026-10-14"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var preflightId = await ConsentPreflightAsync(client);
        var badCommit = CoachWeeklyCommitSchemaTests.BuildCommitJson(
            dayTypeForIndex: i => i == 2 ? "easy" : (i == 6 ? "long" : "rest"));
        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true, badCommit, 1, 1, CoachProtocolBrainError.None, null));

        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId });
        var started = await generate.Content.ReadFromJsonAsync<JobResponse>(JsonOptions);
        var job = await PollUntilTerminalAsync(client, started!.Id);

        job.Status.Should().Be("failed");
        job.ErrorCode.Should().Be("validation");
        job.ErrorMessage.Should().Contain("Blackout");
        job.SchemaRepairAttempted.Should().BeFalse();
        _factory.Brain.CallCount.Should().Be(1);
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
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public bool SchemaRepairAttempted { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed class WeekResponse
    {
        public string Status { get; set; } = "";
        public string PayloadHash { get; set; } = "";
    }
}

public sealed class CoachGenerateTestFactory : IDisposable
{
    private readonly TempoWebApplicationFactory _inner = new();

    public FakeAnthropicCoachClient ProbeFake { get; } = new();
    public FakeCoachProtocolBrain Brain { get; } = new();

    public WebApplicationFactory<Program> Decorated { get; }

    public CoachGenerateTestFactory()
    {
        Decorated = _inner.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var coachConfig = services.SingleOrDefault(s => s.ServiceType == typeof(CoachConfig));
                if (coachConfig != null)
                {
                    services.Remove(coachConfig);
                }

                services.AddSingleton(new CoachConfig { Enabled = true });

                var probe = services.SingleOrDefault(s => s.ServiceType == typeof(IAnthropicCoachClient));
                if (probe != null)
                {
                    services.Remove(probe);
                }

                services.AddSingleton<IAnthropicCoachClient>(ProbeFake);

                var brain = services.SingleOrDefault(s => s.ServiceType == typeof(ICoachProtocolBrain));
                if (brain != null)
                {
                    services.Remove(brain);
                }

                services.AddSingleton<ICoachProtocolBrain>(Brain);
            });
        });
    }

    public void Dispose()
    {
        Decorated.Dispose();
        _inner.Dispose();
    }
}
