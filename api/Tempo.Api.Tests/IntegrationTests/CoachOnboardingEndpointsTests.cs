using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class CoachOnboardingEndpointsTests : IClassFixture<CoachEnabledTestFactory>, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachEnabledTestFactory _factory;
    private readonly TempoWebApplicationFactory _disabledFactory;

    public CoachOnboardingEndpointsTests(CoachEnabledTestFactory factory)
    {
        _factory = factory;
        _disabledFactory = new TempoWebApplicationFactory();
    }

    public void Dispose()
    {
        _disabledFactory.Dispose();
    }

    private async Task<HttpClient> AuthenticatedClientAsync(
        WebApplicationFactory<Program>? factory = null,
        bool tempoOnboardingCompleted = true)
    {
        var app = factory ?? _factory.Decorated;
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "testuser");
        if (user == null)
        {
            user = await TestDataSeeder.SeedUserAsync(db, "testuser");
        }

        user.OnboardingCompleted = tempoOnboardingCompleted;
        await db.SaveChangesAsync();

        return await TestHttpClientFactory.CreateAuthenticatedClientAsync(app);
    }

    private static async Task OptInAsync(HttpClient client)
    {
        var response = await client.PostAsync("/settings/coach/opt-in", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static object CompletePayload(
        string type = "race",
        string distance = "Half-Marathon",
        string? raceDate = "2027-01-09",
        int? targetTimeS = 5400,
        int easy = 360,
        int tempo = 310,
        int threshold = 280,
        int interval = 265)
    {
        return new
        {
            goal = new
            {
                type,
                distance,
                raceDate,
                targetTimeS,
                notes = "A-goal"
            },
            paces = new
            {
                easySecPerKm = easy,
                tempoSecPerKm = tempo,
                thresholdSecPerKm = threshold,
                intervalSecPerKm = interval
            },
            hrZones = new
            {
                calculationMethod = "AgeBased",
                age = 30,
                restingHeartRateBpm = (int?)null,
                maxHeartRateBpm = 190,
                zones = new[]
                {
                    new { zoneNumber = 1, minBpm = 95, maxBpm = 114 },
                    new { zoneNumber = 2, minBpm = 114, maxBpm = 133 },
                    new { zoneNumber = 3, minBpm = 133, maxBpm = 152 },
                    new { zoneNumber = 4, minBpm = 152, maxBpm = 171 },
                    new { zoneNumber = 5, minBpm = 171, maxBpm = 190 }
                }
            }
        };
    }

    [Fact]
    public async Task Onboarding_WhenHostDisabled_ReturnsNotFound()
    {
        var client = await AuthenticatedClientAsync(_disabledFactory);

        (await client.GetAsync("/coach/onboarding")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/coach/onboarding/prefill")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/coach/onboarding", CompletePayload()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Onboarding_WhenNotOptedIn_ReturnsNotFound()
    {
        var client = await AuthenticatedClientAsync();

        (await client.GetAsync("/coach/onboarding")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/coach/onboarding/prefill")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/coach/onboarding", CompletePayload()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PrefillAndComplete_WhenTempoOnboardingIncomplete_ReturnBadRequest()
    {
        var client = await AuthenticatedClientAsync(tempoOnboardingCompleted: false);
        await OptInAsync(client);

        var prefill = await client.GetAsync("/coach/onboarding/prefill");
        prefill.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var prefillBody = await prefill.Content.ReadAsStringAsync();
        prefillBody.Should().Contain("Tempo onboarding");

        var complete = await client.PostAsJsonAsync("/coach/onboarding", CompletePayload());
        complete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var completeBody = await complete.Content.ReadAsStringAsync();
        completeBody.Should().Contain("Tempo onboarding");
    }

    [Fact]
    public async Task GetOnboarding_WhenOptedIn_Empty_ReturnsNotCompleted()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.GetAsync("/coach/onboarding");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OnboardingResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Completed.Should().BeFalse();
        body.Goal.Should().BeNull();
        body.Layer1.Should().BeNull();
        body.Disclaimer.Should().Contain("not a doctor");
    }

    [Fact]
    public async Task Prefill_ReturnsTempoSnapshotAndSuggestedPaces()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        Shoe shoe;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus");
            await TestDataSeeder.SeedUserSettingsAsync(db, defaultShoeId: shoe.Id, age: 32);
            var race = await TestDataSeeder.SeedWorkoutAsync(
                db,
                distanceM: 5000,
                durationS: 1200,
                startedAt: DateTime.UtcNow.AddDays(-14));
            race.RunType = "Race";
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/coach/onboarding/prefill");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrefillResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.UnitPreference.Should().Be("metric");
        body.HrZones.Should().NotBeNull();
        body.HrZones!.Zones.Should().HaveCount(5);
        body.Shoes.Should().ContainSingle(s => s.Id == shoe.Id && s.Brand == "Nike");
        body.DefaultShoeId.Should().Be(shoe.Id);
        body.SuggestedPaces.Should().NotBeNull();
        body.SuggestedPaces!.ThresholdSecPerKm.Should().Be(240); // recent 5K race 1200s / 5km
        body.SuggestedPaces.EasySecPerKm.Should().Be(312); // 240 * 1.3
        body.GoalDistances.Should().Contain("Half-Marathon");
        body.Disclaimer.Should().Contain("not a doctor");
    }

    [Fact]
    public async Task Complete_RaceGoal_PersistsGoalAndLayer1_RaceStatusMirrors()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.PostAsJsonAsync("/coach/onboarding", CompletePayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OnboardingResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Completed.Should().BeTrue();
        body.Goal.Should().NotBeNull();
        body.Goal!.Type.Should().Be("race");
        body.Goal.Distance.Should().Be("Half-Marathon");
        body.Goal.DistanceM.Should().Be(21097.5);
        body.Goal.RaceDate.Should().Be("2027-01-09");
        body.Layer1.Should().NotBeNull();
        body.Layer1!.RaceStatus.GoalType.Should().Be("race");
        body.Layer1.RaceStatus.Distance.Should().Be("Half-Marathon");
        body.Layer1.RaceStatus.RaceDate.Should().Be("2027-01-09");
        body.Layer1.Paces.EasySecPerKm.Should().Be(360);
        body.Disclaimer.Should().Contain("not a doctor");

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CoachGoals.CountAsync()).Should().Be(1);
        (await db.CoachMemoryLayers.CountAsync(l => l.Layer == "L1")).Should().Be(1);
    }

    [Fact]
    public async Task Complete_PrGoal_RequiresTargetTime()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var missing = await client.PostAsJsonAsync(
            "/coach/onboarding",
            CompletePayload(type: "pr", raceDate: null, targetTimeS: null));
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await client.PostAsJsonAsync(
            "/coach/onboarding",
            CompletePayload(type: "pr", distance: "5K", raceDate: null, targetTimeS: 1140));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ok.Content.ReadFromJsonAsync<OnboardingResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Completed.Should().BeTrue();
        body.Goal!.Type.Should().Be("pr");
        body.Goal.TargetTimeS.Should().Be(1140);
        body.Layer1!.RaceStatus.GoalType.Should().Be("pr");
        body.Layer1.RaceStatus.TargetTimeS.Should().Be(1140);
    }

    [Fact]
    public async Task Complete_WithoutConfirmedPaces_ReturnsBadRequest()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.PostAsJsonAsync("/coach/onboarding", new
        {
            goal = new { type = "race", distance = "5K", raceDate = "2027-06-01" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("paces");
    }

    [Fact]
    public async Task Complete_Upserts_SingleGoalAndLayer1()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        (await client.PostAsJsonAsync("/coach/onboarding", CompletePayload(distance: "5K", raceDate: "2026-12-01")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync(
            "/coach/onboarding",
            CompletePayload(type: "pr", distance: "10K", raceDate: null, targetTimeS: 2400, easy: 350));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await second.Content.ReadFromJsonAsync<OnboardingResponse>(JsonOptions);
        body!.Goal!.Type.Should().Be("pr");
        body.Goal.Distance.Should().Be("10K");
        body.Layer1!.Paces.EasySecPerKm.Should().Be(350);

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CoachGoals.CountAsync()).Should().Be(1);
        (await db.CoachMemoryLayers.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Home_DropsOnboardingNextStep_WhenCompleted()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var before = await client.GetFromJsonAsync<HomeResponse>("/coach", JsonOptions);
        before!.NextSteps.Should().Contain("Complete coach onboarding");
        before.OnboardingCompleted.Should().BeFalse();

        (await client.PostAsJsonAsync("/coach/onboarding", CompletePayload()))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<HomeResponse>("/coach", JsonOptions);
        after!.NextSteps.Should().NotContain("Complete coach onboarding");
        after.OnboardingCompleted.Should().BeTrue();
        after.Goal.Should().NotBeNull();
        after.Goal!.Distance.Should().Be("Half-Marathon");
    }

    [Fact]
    public async Task ShoeDelete_DoesNotCascadeBreakCoachLayer1Snapshot()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        Shoe shoe;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Saucony", "Endorphin");
        }

        var payload = new
        {
            goal = new { type = "race", distance = "5K", raceDate = "2027-03-01", notes = (string?)null },
            paces = new
            {
                easySecPerKm = 360,
                tempoSecPerKm = 310,
                thresholdSecPerKm = 280,
                intervalSecPerKm = 265
            },
            shoes = new[] { new { id = shoe.Id, brand = shoe.Brand, model = shoe.Model } },
            defaultShoeId = shoe.Id
        };

        (await client.PostAsJsonAsync("/coach/onboarding", payload)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var row = await db.Shoes.SingleAsync(s => s.Id == shoe.Id);
            db.Shoes.Remove(row);
            await db.SaveChangesAsync();
        }

        var status = await client.GetFromJsonAsync<OnboardingResponse>("/coach/onboarding", JsonOptions);
        status!.Completed.Should().BeTrue();
        status.Layer1!.Shoes.Should().ContainSingle(s =>
            s.Id == shoe.Id && s.Brand == "Saucony" && s.Model == "Endorphin");
    }

    private class OnboardingResponse
    {
        public bool Completed { get; set; }
        public GoalResponse? Goal { get; set; }
        public Layer1Response? Layer1 { get; set; }
        public string Disclaimer { get; set; } = string.Empty;
    }

    private class GoalResponse
    {
        public string Type { get; set; } = string.Empty;
        public string Distance { get; set; } = string.Empty;
        public double DistanceM { get; set; }
        public string? RaceDate { get; set; }
        public int? TargetTimeS { get; set; }
        public string? Notes { get; set; }
    }

    private class Layer1Response
    {
        public PacesResponse Paces { get; set; } = new();
        public RaceStatusResponse RaceStatus { get; set; } = new();
        public List<ShoeResponse> Shoes { get; set; } = new();
    }

    private class PacesResponse
    {
        public int EasySecPerKm { get; set; }
        public int TempoSecPerKm { get; set; }
        public int ThresholdSecPerKm { get; set; }
        public int IntervalSecPerKm { get; set; }
    }

    private class RaceStatusResponse
    {
        public string GoalType { get; set; } = string.Empty;
        public string Distance { get; set; } = string.Empty;
        public string? RaceDate { get; set; }
        public int? TargetTimeS { get; set; }
    }

    private class ShoeResponse
    {
        public Guid Id { get; set; }
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    private class PrefillResponse
    {
        public string UnitPreference { get; set; } = string.Empty;
        public HrZonesResponse? HrZones { get; set; }
        public List<ShoeResponse> Shoes { get; set; } = new();
        public Guid? DefaultShoeId { get; set; }
        public List<BestEffortResponse> BestEfforts { get; set; } = new();
        public PacesResponse? SuggestedPaces { get; set; }
        public List<string> GoalDistances { get; set; } = new();
        public string Disclaimer { get; set; } = string.Empty;
    }

    private class HrZonesResponse
    {
        public List<ZoneResponse> Zones { get; set; } = new();
    }

    private class ZoneResponse
    {
        public int ZoneNumber { get; set; }
        public int MinBpm { get; set; }
        public int MaxBpm { get; set; }
    }

    private class BestEffortResponse
    {
        public string Distance { get; set; } = string.Empty;
        public int TimeS { get; set; }
    }

    private class HomeResponse
    {
        public List<string> NextSteps { get; set; } = new();
        public bool OnboardingCompleted { get; set; }
        public GoalResponse? Goal { get; set; }
    }
}
