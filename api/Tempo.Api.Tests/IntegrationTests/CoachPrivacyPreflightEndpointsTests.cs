using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class CoachPrivacyPreflightEndpointsTests : IClassFixture<CoachEnabledTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachEnabledTestFactory _factory;

    public CoachPrivacyPreflightEndpointsTests(CoachEnabledTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
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

    private static async Task OptInAsync(HttpClient client)
    {
        var response = await client.PostAsync("/settings/coach/opt-in", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task SaveEmptyCheckInAsync(HttpClient client, string referenceDate = "2026-10-13")
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

    [Fact]
    public async Task Preflight_WhenHostDisabled_ReturnsNotFound()
    {
        using var disabled = new TempoWebApplicationFactory();
        using var scope = disabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(disabled);

        (await client.PostAsync("/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate=2026-10-13", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Preflight_WhenNotOptedIn_ReturnsNotFound()
    {
        var client = await AuthenticatedClientAsync();

        (await client.PostAsync("/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate=2026-10-13", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assemble_WithoutCheckIn_ReturnsBadRequest()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var assemble = await client.PostAsync(
            "/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate=2026-10-13",
            null);
        assemble.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await assemble.Content.ReadAsStringAsync();
        body.Should().Contain("Save a Coach check-in");
    }

    [Fact]
    public async Task AssembleEmptyWeek_ThenConsent()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);
        await SaveEmptyCheckInAsync(client);

        var assemble = await client.PostAsync(
            "/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate=2026-10-13",
            null);
        assemble.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await assemble.Content.ReadFromJsonAsync<PreflightResponse>(JsonOptions);
        doc.Should().NotBeNull();
        doc!.WeekStartDate.Should().Be("2026-10-19");
        doc.CheckInWeekStartDate.Should().Be("2026-10-12");
        doc.CheckInWeekEndDate.Should().Be("2026-10-18");
        doc.PlanWeekStartDate.Should().Be("2026-10-19");
        doc.PlanWeekEndDate.Should().Be("2026-10-25");
        doc.Consented.Should().BeFalse();
        doc.ConsentedAt.Should().BeNull();
        doc.Report.Included.Should().NotBeEmpty();
        doc.Report.Stripped.Should().Contain(s => s.Category == "gpsRoutes");
        doc.RedactedPayload.Should().NotBeNull();

        var consent = await client.PostAsync($"/coach/privacy/preflight/{doc.Id}/consent", null);
        consent.StatusCode.Should().Be(HttpStatusCode.OK);
        var consented = await consent.Content.ReadFromJsonAsync<PreflightResponse>(JsonOptions);
        consented!.Consented.Should().BeTrue();
        consented.ConsentedAt.Should().NotBeNull();

        var get = await client.GetAsync($"/coach/privacy/preflight/{doc.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await get.Content.ReadFromJsonAsync<PreflightResponse>(JsonOptions);
        again!.Consented.Should().BeTrue();
        again.PlanWeekStartDate.Should().Be("2026-10-19");
        again.CheckInWeekStartDate.Should().Be("2026-10-12");
    }

    [Fact]
    public async Task Assemble_WithNamedWorkout_DoesNotLeakNameInPayload()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);
        await SaveEmptyCheckInAsync(client, "2026-10-14");

        const string secretName = "SecretRunNameXYZ";
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc),
                distanceM: 8000,
                durationS: 2400,
                name: secretName);
            workout.Notes = "solid effort";
            workout.AvgHeartRateBpm = 148;
            await db.SaveChangesAsync();
        }

        var assemble = await client.PostAsync(
            "/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate=2026-10-14",
            null);
        assemble.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await assemble.Content.ReadAsStringAsync();
        raw.Should().NotContain(secretName);
        raw.Should().NotContain("\"name\":");

        var doc = await assemble.Content.ReadFromJsonAsync<PreflightResponse>(JsonOptions);
        doc!.Consented.Should().BeFalse();
        var payloadJson = JsonSerializer.Serialize(doc.RedactedPayload);
        payloadJson.Should().Contain("8000");
        payloadJson.Should().Contain("solid effort");
    }

    [Fact]
    public async Task Assemble_IncludesSelectedEpisodesAndActiveRules_ScrubsUsername()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            db.CoachEpisodes.AddRange(
                new CoachEpisode
                {
                    Id = "ep_2026-10-10_race_prior",
                    Date = new DateOnly(2026, 10, 10),
                    Category = CoachEpisodeCategories.Race,
                    Summary = "Prior week race for testuser",
                    SourceWeekEnding = new DateOnly(2026, 10, 11),
                    Source = CoachEpisodeSources.Coach,
                    CreatedAt = DateTime.UtcNow
                },
                new CoachEpisode
                {
                    Id = "ep_2026-08-01_issue_old",
                    Date = new DateOnly(2026, 8, 1),
                    Category = CoachEpisodeCategories.Issue,
                    Summary = "Old calf niggle flare",
                    Source = CoachEpisodeSources.Athlete,
                    CreatedAt = DateTime.UtcNow
                },
                new CoachEpisode
                {
                    Id = "ep_retired",
                    Date = new DateOnly(2026, 9, 1),
                    Category = CoachEpisodeCategories.Milestone,
                    Summary = "Should not appear",
                    Retired = true,
                    Source = CoachEpisodeSources.Coach,
                    CreatedAt = DateTime.UtcNow
                });
            db.CoachRules.AddRange(
                new CoachRule
                {
                    Id = "rule_active_heat",
                    TargetLayer = CoachRuleTargetLayers.DataInterpretationNotes,
                    RuleText = "AM MP recovers cleaner than PM",
                    Status = CoachRuleStatuses.Active,
                    Evidence = ["Aug contrast"],
                    Source = CoachRuleSources.Coach,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new CoachRule
                {
                    Id = "rule_retired",
                    TargetLayer = CoachRuleTargetLayers.KeyLearnings,
                    RuleText = "Retired rule",
                    Status = CoachRuleStatuses.Retired,
                    Evidence = ["old"],
                    Source = CoachRuleSources.Coach,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            db.CoachCheckIns.Add(new CoachCheckIn
            {
                WeekStartDate = new DateOnly(2026, 10, 12),
                Niggles = "calf tight",
                RunNotesJson = "[]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Check-in already seeded above for week 2026-10-12.
        var assemble = await client.PostAsync(
            "/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate=2026-10-13",
            null);
        assemble.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await assemble.Content.ReadFromJsonAsync<PreflightResponse>(JsonOptions);
        doc.Should().NotBeNull();
        doc!.CheckInWeekStartDate.Should().Be("2026-10-12");
        doc.PlanWeekStartDate.Should().Be("2026-10-19");

        var payloadJson = JsonSerializer.Serialize(doc.RedactedPayload);
        using var payload = JsonDocument.Parse(payloadJson);
        var meta = payload.RootElement.GetProperty("meta");
        meta.GetProperty("checkInWeekStartDate").GetString().Should().Be("2026-10-12");
        meta.GetProperty("planWeekStartDate").GetString().Should().Be("2026-10-19");
        // L2 prior-week keyed off check-in week ending (2026-10-18) → prior ending 2026-10-11.
        var layer2 = payload.RootElement.GetProperty("layer2");
        layer2.TryGetProperty("scoresById", out _).Should().BeFalse();
        layer2.GetProperty("selection").GetProperty("priorWeekEnding").GetString().Should().Be("2026-10-11");
        layer2.GetProperty("priorWeek").EnumerateArray().Select(e => e.GetProperty("id").GetString())
            .Should().Contain("ep_2026-10-10_race_prior");
        layer2.GetProperty("retrieved").EnumerateArray().Select(e => e.GetProperty("id").GetString())
            .Should().Contain("ep_2026-08-01_issue_old");
        payloadJson.Should().NotContain("ep_retired");
        payloadJson.Should().NotContain("testuser");

        var rules = payload.RootElement.GetProperty("layer3").GetProperty("rules");
        rules.EnumerateArray().Select(r => r.GetProperty("id").GetString())
            .Should().Contain("rule_active_heat")
            .And.NotContain("rule_retired");

        doc.Report.SelectedEpisodeIds.Should().Contain("ep_2026-10-10_race_prior");
        doc.Report.SelectedRuleIds.Should().Contain("rule_active_heat");
        doc.Report.Included.Should().Contain(i =>
            i.Category == "layer2" && i.Detail.Contains("episodes selected of"));
        doc.Report.Included.Should().Contain(i =>
            i.Category == "layer3" && i.Detail.Contains("1 active standing rules"));
    }

    private sealed class PreflightResponse
    {
        public Guid Id { get; set; }
        public string WeekStartDate { get; set; } = "";
        public string CheckInWeekStartDate { get; set; } = "";
        public string CheckInWeekEndDate { get; set; } = "";
        public string PlanWeekStartDate { get; set; } = "";
        public string PlanWeekEndDate { get; set; } = "";
        public bool Consented { get; set; }
        public DateTime? ConsentedAt { get; set; }
        public object? RedactedPayload { get; set; }
        public ReportDto Report { get; set; } = new();
    }

    private sealed class ReportDto
    {
        public List<ReportItemDto> Included { get; set; } = [];
        public List<ReportItemDto> Stripped { get; set; } = [];
        public List<string> SelectedEpisodeIds { get; set; } = [];
        public List<string> SelectedRuleIds { get; set; } = [];
    }

    private sealed class ReportItemDto
    {
        public string Category { get; set; } = "";
        public string Detail { get; set; } = "";
    }
}
