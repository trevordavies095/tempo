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
public class CoachExchangeEndpointsTests : IClassFixture<CoachGenerateTestFactory>
{
    private static readonly DateOnly PlanMonday = new(2026, 10, 19);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachGenerateTestFactory _factory;

    public CoachExchangeEndpointsTests(CoachGenerateTestFactory factory)
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

    private static async Task<JobResponse> PollJobAsync(HttpClient client, Guid jobId, TimeSpan? timeout = null)
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

    private static async Task<ExchangeResponse> PollExchangeAsync(
        HttpClient client,
        Guid exchangeId,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/coach/exchanges/{exchangeId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var doc = await response.Content.ReadFromJsonAsync<ExchangeResponse>(JsonOptions);
            doc.Should().NotBeNull();
            if (doc!.Status is "ready" or "failed")
            {
                return doc;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Exchange {exchangeId} did not reach a terminal status");
    }

    [Fact]
    public async Task Send_UnattachedWithText_ReachesReadyWithSectionedReply_BrainNotCalledBeforeConsent()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        _factory.Brain.CallCount.Should().Be(0);

        var preview = await client.PostAsJsonAsync("/coach/exchanges/preflight", new
        {
            athleteText = "Legs feel heavy after yesterday.",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Brain.CallCount.Should().Be(0);

        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(),
            5,
            8,
            CoachProtocolBrainError.None,
            null));

        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Legs feel heavy after yesterday.",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await send.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        started.Should().NotBeNull();
        started!.Job.Kind.Should().Be("exchange");
        started.Exchange.AthleteText.Should().Be("Legs feel heavy after yesterday.");
        started.Exchange.WeekStartDate.Should().Be(PlanMonday.ToString("yyyy-MM-dd"));

        var exchange = await PollExchangeAsync(client, started.Exchange.Id);
        exchange.Status.Should().Be("ready");
        exchange.Reply.Should().NotBeNull();
        exchange.Reply!.Value.GetProperty("verdict").GetString().Should().Be("on_track");
        exchange.Reply.Value.GetProperty("whatHappened").GetString().Should().NotBeNullOrWhiteSpace();
        _factory.Brain.ExchangeCallCount.Should().Be(1);

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CoachWeeks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Send_EmptyText_ReturnsBadRequest()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "   ",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        send.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Brain.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_WhenCoachDisabled_ReturnsNotFound()
    {
        // Use a temporary factory with Coach disabled.
        using var inner = new TempoWebApplicationFactory();
        using var decorated = inner.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var coachConfig = services.SingleOrDefault(s => s.ServiceType == typeof(CoachConfig));
                if (coachConfig != null)
                {
                    services.Remove(coachConfig);
                }

                services.AddSingleton(new CoachConfig { Enabled = false });
            });
        });

        using var scope = decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "testuser")
                   ?? await TestDataSeeder.SeedUserAsync(db, "testuser");
        user.OnboardingCompleted = true;
        await db.SaveChangesAsync();

        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(decorated);
        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "hello",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        send.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Send_WhenNotOptedIn_ReturnsNotFound()
    {
        var client = await AuthenticatedClientAsync();
        // No opt-in
        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "hello",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        send.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Brain.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SecondConcurrentExchange_ReturnsConflict_RitualMayRunAlongside()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Brain.ExchangeHandler = (_, _) =>
        {
            gate.Task.GetAwaiter().GetResult();
            return new CoachProtocolBrainResult(
                true,
                CoachExchangeReplySchemaTests.BuildReplyJson(),
                1,
                1,
                CoachProtocolBrainError.None,
                null);
        };

        var first = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "First message",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstDoc = await first.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);

        // Wait until exchange job is running
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var job = await client.GetFromJsonAsync<JobResponse>($"/coach/jobs/{firstDoc!.Job.Id}", JsonOptions);
            if (job!.Status == "running")
            {
                break;
            }

            await Task.Delay(20);
        }

        var second = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Second message",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Ritual generate should still be allowed while exchange is active.
        await client.PutAsJsonAsync("/coach/check-in", new
        {
            referenceDate = "2026-10-13",
            timezoneOffsetMinutes = 0,
            overallFeel = (string?)null,
            niggles = (string?)null,
            lifeConstraints = (string?)null,
            runNotes = Array.Empty<object>()
        });
        var assemble = await client.PostAsync(
            "/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate=2026-10-13",
            null);
        assemble.StatusCode.Should().Be(HttpStatusCode.OK);
        var preflight = await assemble.Content.ReadFromJsonAsync<PreflightIdResponse>(JsonOptions);
        (await client.PostAsync($"/coach/privacy/preflight/{preflight!.Id}/consent", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Brain.Results.Add(new CoachProtocolBrainResult(
            true,
            CoachWeeklyCommitSchemaTests.BuildCommitJson(weekStart: PlanMonday),
            1,
            1,
            CoachProtocolBrainError.None,
            null));

        var generate = await client.PostAsJsonAsync("/coach/generate", new { preflightId = preflight.Id });
        generate.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Current ritual job must not be the exchange.
        var current = await client.GetFromJsonAsync<JobResponse>("/coach/jobs/current", JsonOptions);
        current!.Kind.Should().NotBe("exchange");

        gate.SetResult();
        await PollExchangeAsync(client, firstDoc!.Exchange.Id);
        await PollJobAsync(client, (await generate.Content.ReadFromJsonAsync<JobResponse>(JsonOptions))!.Id);
    }

    [Fact]
    public async Task MemoryChannelsInReply_FailValidation()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        var bad = """
            {
              "whatHappened": "ok",
              "whyItLooksLikeThat": "ok",
              "whatItMeansNext": "ok",
              "verdict": "on_track",
              "safetyFlags": [],
              "suggestedChange": null,
              "layer2EpisodeDrafts": [{ "date": "2026-10-19", "category": "issue", "summary": "nope" }]
            }
            """;
        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true, bad, 1, 1, CoachProtocolBrainError.None, null));
        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true, bad, 1, 1, CoachProtocolBrainError.None, null));

        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Any update?",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        var started = await send.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        var exchange = await PollExchangeAsync(client, started!.Exchange.Id);
        exchange.Status.Should().Be("failed");
        exchange.ErrorCode.Should().Be("validation");
        exchange.ErrorMessage.Should().Contain("memory");

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CoachWeeks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ListGetReceipt_AndRetry_PreservesAthleteText()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            false, null, null, null, CoachProtocolBrainError.Unknown, "boom"));
        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            false, null, null, null, CoachProtocolBrainError.Unknown, "boom"));

        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Keep this text on retry",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        var started = await send.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        var failed = await PollExchangeAsync(client, started!.Exchange.Id);
        failed.Status.Should().Be("failed");
        failed.AthleteText.Should().Be("Keep this text on retry");

        var list = await client.GetFromJsonAsync<List<ExchangeResponse>>(
            $"/coach/exchanges?weekStartDate={PlanMonday:yyyy-MM-dd}",
            JsonOptions);
        list.Should().ContainSingle(e => e.Id == failed.Id);

        var receipt = await client.GetAsync($"/coach/exchanges/{failed.Id}/privacy-receipt");
        receipt.StatusCode.Should().Be(HttpStatusCode.OK);
        var receiptDoc = await receipt.Content.ReadFromJsonAsync<ReceiptResponse>(JsonOptions);
        receiptDoc!.PayloadHash.Should().NotBeNullOrEmpty();
        receiptDoc.ConsentedAt.Should().NotBeNull();

        _factory.Brain.Reset();
        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(verdict: "watch"),
            2,
            3,
            CoachProtocolBrainError.None,
            null));

        var retry = await client.PostAsJsonAsync($"/coach/exchanges/{failed.Id}/retry", new
        {
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        retry.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var retried = await retry.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        retried!.Exchange.AthleteText.Should().Be("Keep this text on retry");

        var ready = await PollExchangeAsync(client, failed.Id);
        ready.Status.Should().Be("ready");
        ready.AthleteText.Should().Be("Keep this text on retry");
        ready.Reply!.Value.GetProperty("verdict").GetString().Should().Be("watch");
    }

    [Fact]
    public async Task DeleteAll_RemovesExchangesAndExchangePrefights()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(),
            1,
            1,
            CoachProtocolBrainError.None,
            null));

        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Wipe me",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        var started = await send.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        await PollExchangeAsync(client, started!.Exchange.Id);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.CoachExchanges.CountAsync()).Should().Be(1);
            (await db.CoachPrivacyPreflights.CountAsync(p => p.Kind == CoachPrivacyPreflightKinds.Exchange))
                .Should().BeGreaterThan(0);
        }

        (await client.DeleteAsync("/settings/coach")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.CoachExchanges.CountAsync()).Should().Be(0);
            (await db.CoachPrivacyPreflights.CountAsync()).Should().Be(0);
            (await db.CoachJobs.CountAsync()).Should().Be(0);
        }
    }

    private sealed class SendResponse
    {
        public JobResponse Job { get; set; } = new();
        public ExchangeResponse Exchange { get; set; } = new();
    }

    private sealed class ExchangeResponse
    {
        public Guid Id { get; set; }
        public string WeekStartDate { get; set; } = "";
        public string AthleteText { get; set; } = "";
        public string Status { get; set; } = "";
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public JsonElement? Reply { get; set; }
    }

    private sealed class JobResponse
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public string Kind { get; set; } = "";
        public Guid? ResultWeekId { get; set; }
    }

    private sealed class PreflightIdResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class ReceiptResponse
    {
        public string PayloadHash { get; set; } = "";
        public DateTime? ConsentedAt { get; set; }
    }
}
