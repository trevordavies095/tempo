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

        var list = await client.GetFromJsonAsync<ExchangeListResponse>(
            $"/coach/exchanges?weekStartDate={PlanMonday:yyyy-MM-dd}",
            JsonOptions);
        list!.WeekLocked.Should().BeFalse();
        list.Exchanges.Should().ContainSingle(e => e.Id == failed.Id);

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
    public async Task Send_WithWorkoutIdAndEmptyText_IncludesAttachedRunWithoutGpsOrDeviceTemp()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        Guid workoutId;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
                distanceM: 8000,
                durationS: 2400,
                name: "Secret Loop Name");
            workout.Weather = """{"temperature":16.0,"humidity":65,"windSpeed":2.5,"source":"open-meteo"}""";
            workout.ElevGainM = 40;
            workout.ElevLossM = 35;
            workout.MovingTimeS = 2300;
            workout.AvgCadenceRpm = 168;
            workout.MaxCadenceRpm = 180;
            workout.AvgHeartRateBpm = 148;
            await db.SaveChangesAsync();
            await TestDataSeeder.SeedWorkoutWithSplitsAsync(db, workout);
            await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(db, workout, intervalSeconds: 5);
            workoutId = workout.Id;
        }

        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(),
            3,
            4,
            CoachProtocolBrainError.None,
            null));

        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "",
            workoutId,
            timezoneOffsetMinutes = 0
        });
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await send.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        started!.Exchange.WorkoutId.Should().Be(workoutId);
        started.Exchange.WeekStartDate.Should().Be(PlanMonday.ToString("yyyy-MM-dd"));

        var exchange = await PollExchangeAsync(client, started.Exchange.Id);
        exchange.Status.Should().Be("ready");

        var receipt = await client.GetFromJsonAsync<ReceiptPayloadResponse>(
            $"/coach/exchanges/{exchange.Id}/privacy-receipt",
            JsonOptions);
        receipt.Should().NotBeNull();
        var payloadJson = receipt!.RedactedPayload.GetRawText();
        using var payload = JsonDocument.Parse(payloadJson);
        var root = payload.RootElement;
        root.TryGetProperty("attachedRun", out var attached).Should().BeTrue();
        attached.ValueKind.Should().NotBe(JsonValueKind.Null);
        attached.GetProperty("splits").GetArrayLength().Should().BeGreaterThan(0);
        attached.GetProperty("series").GetArrayLength().Should().BeGreaterThan(0);
        attached.GetProperty("series").GetArrayLength().Should().BeLessThanOrEqualTo(90);
        attached.TryGetProperty("weather", out var weather).Should().BeTrue();
        weather.GetProperty("temperatureC").GetDouble().Should().Be(16.0);
        weather.TryGetProperty("dewPointC", out _).Should().BeTrue();

        payloadJson.Should().NotContain("Secret Loop Name");
        payloadJson.Should().NotContain("deviceTemperature");
        // Series points must not include device/skin temperature.
        foreach (var point in attached.GetProperty("series").EnumerateArray())
        {
            point.TryGetProperty("temperatureC", out _).Should().BeFalse();
        }

        CoachPrivacyRedactor.LooksLikeResidualGps(payloadJson).Should().BeFalse();

        // recentTraining: prior week count 1 → two buckets (W−1…current)
        root.GetProperty("recentTraining").GetProperty("weeks").GetArrayLength().Should().Be(2);

        var reportJson = receipt.PrivacyReport.GetRawText();
        reportJson.Should().Contain("attachedRun");
        reportJson.Should().Contain("deviceTemperature");
        reportJson.Should().NotContain("hrStreams");
    }

    [Fact]
    public async Task Send_AttachedPlanless_StripsSuggestedChange_PlannedNull()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        Guid workoutId;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800);
            workoutId = workout.Id;
        }

        var suggested = new
        {
            summary = "Ease Thursday",
            note = "Swap Thursday for easy",
            affectedDates = new[] { "2026-10-23" }
        };
        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(suggestedChange: suggested),
            1,
            1,
            CoachProtocolBrainError.None,
            null));

        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            workoutId,
            timezoneOffsetMinutes = 0
        });
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await send.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        var exchange = await PollExchangeAsync(client, started!.Exchange.Id);
        exchange.Status.Should().Be("ready");
        exchange.Reply!.Value.TryGetProperty("suggestedChange", out var sc).Should().BeTrue();
        sc.ValueKind.Should().Be(JsonValueKind.Null);

        var receipt = await client.GetFromJsonAsync<ReceiptPayloadResponse>(
            $"/coach/exchanges/{exchange.Id}/privacy-receipt",
            JsonOptions);
        using var payload = JsonDocument.Parse(receipt!.RedactedPayload.GetRawText());
        var attached = payload.RootElement.GetProperty("attachedRun");
        attached.TryGetProperty("planned", out var planned).Should().BeTrue();
        planned.ValueKind.Should().Be(JsonValueKind.Null);

        // Ask for this change is athlete-gated: exchange Send never auto-starts adjust.
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.CoachAdjustments.CountAsync()).Should().Be(0);
            (await db.CoachJobs.CountAsync(j => j.Kind == CoachJobKinds.Adjust)).Should().Be(0);
        }
    }

    [Fact]
    public async Task Send_WithSuggestedChange_DoesNotAutoEnqueueAdjust()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        // Unattached message — brain may return suggestedChange; still no adjust job.
        var suggested = new
        {
            summary = "Cut volume",
            note = "Drop Friday long to easy 40",
            affectedDates = new[] { "2026-10-23" }
        };
        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(suggestedChange: suggested),
            1,
            1,
            CoachProtocolBrainError.None,
            null));

        var send = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Feeling cooked this week",
            timezoneOffsetMinutes = 0,
            referenceDate = "2026-10-20"
        });
        send.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await send.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        var exchange = await PollExchangeAsync(client, started!.Exchange.Id);
        exchange.Status.Should().Be("ready");
        started.Job.Kind.Should().Be("exchange");

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CoachAdjustments.CountAsync()).Should().Be(0);
        (await db.CoachJobs.CountAsync(j => j.Kind == CoachJobKinds.Adjust)).Should().Be(0);
        (await db.CoachJobs.CountAsync(j => j.Kind == CoachJobKinds.Exchange)).Should().Be(1);
    }

    [Fact]
    public async Task Send_MultipleExchangesSameWorkout_SecondSeesPriorWeekExchanges()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        Guid workoutId;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc));
            workoutId = workout.Id;
        }

        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(verdict: "on_track"),
            1,
            1,
            CoachProtocolBrainError.None,
            null));
        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(verdict: "watch"),
            1,
            1,
            CoachProtocolBrainError.None,
            null));

        var first = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "First look",
            workoutId,
            timezoneOffsetMinutes = 0
        });
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstDoc = await first.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        await PollExchangeAsync(client, firstDoc!.Exchange.Id);

        string? secondPacket = null;
        _factory.Brain.ExchangeHandler = (packet, _) =>
        {
            secondPacket = packet;
            return new CoachProtocolBrainResult(
                true,
                CoachExchangeReplySchemaTests.BuildReplyJson(verdict: "watch"),
                1,
                1,
                CoachProtocolBrainError.None,
                null);
        };

        var second = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Follow-up",
            workoutId,
            timezoneOffsetMinutes = 0
        });
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var secondDoc = await second.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        await PollExchangeAsync(client, secondDoc!.Exchange.Id);

        secondPacket.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(secondPacket!);
        var weekExchanges = doc.RootElement.GetProperty("weekExchanges");
        weekExchanges.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        weekExchanges[0].GetProperty("athleteText").GetString().Should().Contain("First");

        var list = await client.GetFromJsonAsync<ExchangeListResponse>(
            $"/coach/exchanges?workoutId={workoutId}",
            JsonOptions);
        list!.Exchanges.Should().HaveCount(2);
        list.Exchanges.Select(e => e.Id).Should().Contain([firstDoc.Exchange.Id, secondDoc.Exchange.Id]);
    }

    [Fact]
    public async Task AcceptNextWeek_LocksPriorWeekSends_ListRemainsReadable()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        var nextMonday = PlanMonday.AddDays(7);
        var priorReference = "2026-10-20"; // Tuesday in PlanMonday week
        var nextReference = "2026-10-27"; // Tuesday in next week

        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(),
            1,
            1,
            CoachProtocolBrainError.None,
            null));

        var priorSend = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Before lock",
            timezoneOffsetMinutes = 0,
            referenceDate = priorReference
        });
        priorSend.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var priorStarted = await priorSend.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        var priorReady = await PollExchangeAsync(client, priorStarted!.Exchange.Id);
        priorReady.Status.Should().Be("ready");
        priorReady.Locked.Should().BeFalse();

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var now = DateTime.UtcNow;
            db.CoachWeeks.Add(new CoachWeek
            {
                WeekStartDate = nextMonday,
                Status = CoachWeekStatuses.Draft,
                CommitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(weekStart: nextMonday),
                PreflightId = Guid.NewGuid(),
                JobId = Guid.NewGuid(),
                PayloadHash = new string('n', 64),
                RedactedPayloadJson = "{}",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        // Draft alone must not lock prior week.
        var stillOpen = await client.PostAsJsonAsync("/coach/exchanges/preflight", new
        {
            athleteText = "Still open while next week is draft",
            timezoneOffsetMinutes = 0,
            referenceDate = priorReference
        });
        stillOpen.StatusCode.Should().Be(HttpStatusCode.OK);

        var apply = await client.PostAsJsonAsync(
            "/coach/weeks/draft/apply",
            CoachWeeklyCommitSchemaTests.EmptyApplyBody(weekStartDate: nextMonday.ToString("yyyy-MM-dd")));
        apply.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var stamped = await db.CoachExchanges.SingleAsync(e => e.Id == priorReady.Id);
            stamped.Locked.Should().BeTrue();
            (await db.CoachExchanges.CountAsync(e => e.WeekStartDate == PlanMonday)).Should().Be(1);
        }

        var lockedSend = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Should be blocked",
            timezoneOffsetMinutes = 0,
            referenceDate = priorReference
        });
        lockedSend.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var lockedBody = await lockedSend.Content.ReadAsStringAsync();
        lockedBody.Should().Contain("locked");

        var lockedPreflight = await client.PostAsJsonAsync("/coach/exchanges/preflight", new
        {
            athleteText = "Should be blocked",
            timezoneOffsetMinutes = 0,
            referenceDate = priorReference
        });
        lockedPreflight.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var list = await client.GetFromJsonAsync<ExchangeListResponse>(
            $"/coach/exchanges?weekStartDate={PlanMonday:yyyy-MM-dd}",
            JsonOptions);
        list!.WeekLocked.Should().BeTrue();
        list.WeekStartDate.Should().Be(PlanMonday.ToString("yyyy-MM-dd"));
        list.Exchanges.Should().ContainSingle(e => e.Id == priorReady.Id);
        list.Exchanges[0].Locked.Should().BeTrue();

        var get = await client.GetFromJsonAsync<ExchangeResponse>(
            $"/coach/exchanges/{priorReady.Id}",
            JsonOptions);
        get!.Locked.Should().BeTrue();
        get.AthleteText.Should().Be("Before lock");

        _factory.Brain.ExchangeResults.Add(new CoachProtocolBrainResult(
            true,
            CoachExchangeReplySchemaTests.BuildReplyJson(verdict: "watch"),
            1,
            1,
            CoachProtocolBrainError.None,
            null));

        var nextWeekSend = await client.PostAsJsonAsync("/coach/exchanges", new
        {
            athleteText = "Next week still open",
            timezoneOffsetMinutes = 0,
            referenceDate = nextReference
        });
        nextWeekSend.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var nextStarted = await nextWeekSend.Content.ReadFromJsonAsync<SendResponse>(JsonOptions);
        nextStarted!.Exchange.WeekStartDate.Should().Be(nextMonday.ToString("yyyy-MM-dd"));
        var nextReady = await PollExchangeAsync(client, nextStarted.Exchange.Id);
        nextReady.Status.Should().Be("ready");
        nextReady.Locked.Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_AttachedWithGpsLookingNotes_FailsClosed()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAndProviderAsync(client);

        Guid workoutId;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc));
            workout.Notes = "Met near [42.3601, -71.0589] for the start";
            await db.SaveChangesAsync();
            workoutId = workout.Id;
        }

        var preview = await client.PostAsJsonAsync("/coach/exchanges/preflight", new
        {
            workoutId,
            athleteText = "",
            timezoneOffsetMinutes = 0
        });
        preview.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await preview.Content.ReadAsStringAsync();
        body.Should().Contain("Redaction");
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

    private sealed class ExchangeListResponse
    {
        public string WeekStartDate { get; set; } = "";
        public bool WeekLocked { get; set; }
        public List<ExchangeResponse> Exchanges { get; set; } = new();
    }

    private sealed class ExchangeResponse
    {
        public Guid Id { get; set; }
        public string WeekStartDate { get; set; } = "";
        public Guid? WorkoutId { get; set; }
        public string AthleteText { get; set; } = "";
        public string Status { get; set; } = "";
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public bool Locked { get; set; }
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

    private sealed class ReceiptPayloadResponse
    {
        public string PayloadHash { get; set; } = "";
        public DateTime? ConsentedAt { get; set; }
        public JsonElement RedactedPayload { get; set; }
        public JsonElement PrivacyReport { get; set; }
    }
}
