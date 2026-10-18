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
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class CoachMemoryEndpointsTests : IClassFixture<CoachEnabledTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachEnabledTestFactory _factory;

    public CoachMemoryEndpointsTests(CoachEnabledTestFactory factory)
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
        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Memory_WhenHostDisabled_ReturnNotFound()
    {
        using var disabled = new TempoWebApplicationFactory();
        using var scope = disabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(disabled);

        (await client.GetAsync("/coach/memory/episodes")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/coach/memory/rules")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Memory_WhenNotOptedIn_ReturnNotFound()
    {
        var client = await AuthenticatedClientAsync();

        (await client.GetAsync("/coach/memory/episodes")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/coach/memory/episodes", new
        {
            date = "2026-09-20",
            category = "race",
            summary = "Race result"
        })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateEpisode_AthleteSource_AndSlugId()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.PostAsJsonAsync("/coach/memory/episodes", new
        {
            date = "2026-09-20",
            category = "race",
            summary = "Rochester Half: textbook pace"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EpisodeResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Source.Should().Be(CoachEpisodeSources.Athlete);
        body.Retired.Should().BeFalse();
        body.Category.Should().Be("race");
        body.Id.Should().StartWith("ep_2026-09-20_race_");
        body.Summary.Should().Be("Rochester Half: textbook pace");
    }

    [Fact]
    public async Task CreateRule_AthleteSource_AndSlugId()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.PostAsJsonAsync("/coach/memory/rules", new
        {
            targetLayer = "key_learnings",
            ruleText = "Left Achilles flares after back-to-back quality",
            evidence = new[] { "2025 season" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RuleResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Source.Should().Be(CoachRuleSources.Athlete);
        body.Status.Should().Be(CoachRuleStatuses.Active);
        body.TargetLayer.Should().Be("key_learnings");
        body.Id.Should().StartWith("rule_");
        body.Evidence.Should().ContainSingle("2025 season");
    }

    [Fact]
    public async Task ListEpisodes_FiltersCategoryAndRetired()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        (await client.PostAsJsonAsync("/coach/memory/episodes", new
        {
            date = "2026-09-20",
            category = "race",
            summary = "Race A"
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        var issue = await client.PostAsJsonAsync("/coach/memory/episodes", new
        {
            date = "2026-09-21",
            category = "issue",
            summary = "Achilles niggle"
        });
        var issueBody = await issue.Content.ReadFromJsonAsync<EpisodeResponse>(JsonOptions);

        (await client.PostAsync($"/coach/memory/episodes/{issueBody!.Id}/retire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var active = await client.GetFromJsonAsync<List<EpisodeResponse>>(
            "/coach/memory/episodes?includeRetired=false",
            JsonOptions);
        active!.Should().ContainSingle();
        active[0].Category.Should().Be("race");

        var races = await client.GetFromJsonAsync<List<EpisodeResponse>>(
            "/coach/memory/episodes?includeRetired=true&category=race",
            JsonOptions);
        races!.Should().ContainSingle();

        var all = await client.GetFromJsonAsync<List<EpisodeResponse>>(
            "/coach/memory/episodes?includeRetired=true",
            JsonOptions);
        all!.Should().HaveCount(2);
    }

    [Fact]
    public async Task Episode_RetireAndUnretire_AreIdempotent()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var created = await client.PostAsJsonAsync("/coach/memory/episodes", new
        {
            date = "2026-10-01",
            category = "milestone",
            summary = "First 50 mile week"
        });
        var body = await created.Content.ReadFromJsonAsync<EpisodeResponse>(JsonOptions);

        (await client.PostAsync($"/coach/memory/episodes/{body!.Id}/retire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/coach/memory/episodes/{body.Id}/retire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var retired = await client.GetFromJsonAsync<List<EpisodeResponse>>(
            "/coach/memory/episodes?includeRetired=true",
            JsonOptions);
        retired!.Single(e => e.Id == body.Id).Retired.Should().BeTrue();

        (await client.PostAsync($"/coach/memory/episodes/{body.Id}/unretire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/coach/memory/episodes/{body.Id}/unretire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var active = await client.GetFromJsonAsync<List<EpisodeResponse>>(
            "/coach/memory/episodes",
            JsonOptions);
        active!.Single(e => e.Id == body.Id).Retired.Should().BeFalse();
    }

    [Fact]
    public async Task Rule_RetireAndUnretire_AreIdempotent()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var created = await client.PostAsJsonAsync("/coach/memory/rules", new
        {
            targetLayer = "coaching_preferences",
            ruleText = "Prefer morning quality in heat"
        });
        var body = await created.Content.ReadFromJsonAsync<RuleResponse>(JsonOptions);

        (await client.PostAsync($"/coach/memory/rules/{body!.Id}/retire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/coach/memory/rules/{body.Id}/retire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await client.GetFromJsonAsync<List<RuleResponse>>(
            "/coach/memory/rules?includeRetired=true",
            JsonOptions);
        listed!.Single(r => r.Id == body.Id).Status.Should().Be(CoachRuleStatuses.Retired);

        (await client.PostAsync($"/coach/memory/rules/{body.Id}/unretire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var active = await client.GetFromJsonAsync<List<RuleResponse>>(
            "/coach/memory/rules",
            JsonOptions);
        active!.Single(r => r.Id == body.Id).Status.Should().Be(CoachRuleStatuses.Active);
    }

    [Fact]
    public async Task Create_ValidationErrors_ReturnBadRequest()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        (await client.PostAsJsonAsync("/coach/memory/episodes", new
        {
            date = "2026-09-20",
            category = "not_a_category",
            summary = "x"
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/coach/memory/episodes", new
        {
            date = "2026-09-20",
            category = "race",
            summary = new string('x', 401)
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/coach/memory/rules", new
        {
            targetLayer = "not_a_layer",
            ruleText = "Something"
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/coach/memory/rules", new
        {
            targetLayer = "key_learnings",
            ruleText = "   "
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateEpisode_AllowsRecapCategory()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.PostAsJsonAsync("/coach/memory/episodes", new
        {
            date = "2026-10-18",
            category = "recap",
            summary = "Archived weekly notes"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EpisodeResponse>(JsonOptions);
        body!.Category.Should().Be("recap");
    }

    [Fact]
    public async Task MemoryNote_Dated_CreatesMilestoneEpisode()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.PostAsJsonAsync("/coach/memory/notes", new
        {
            text = "Boston qualifier attempt",
            date = "2026-09-15"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MemoryNoteResponse>(JsonOptions);
        body!.Kind.Should().Be("episode");
        body.Episode.Should().NotBeNull();
        body.Episode!.Category.Should().Be("milestone");
        body.Episode.Summary.Should().Be("Boston qualifier attempt");
        body.Episode.Source.Should().Be(CoachEpisodeSources.Athlete);
        body.Episode.Date.Should().Be("2026-09-15");
        body.Rule.Should().BeNull();

        var list = await client.GetFromJsonAsync<List<EpisodeResponse>>(
            "/coach/memory/episodes",
            JsonOptions);
        list!.Should().Contain(e => e.Id == body.Episode.Id);
    }

    [Fact]
    public async Task MemoryNote_Undated_CreatesCoachingPreferencesRule()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.PostAsJsonAsync("/coach/memory/notes", new
        {
            text = "Prefer morning long runs"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MemoryNoteResponse>(JsonOptions);
        body!.Kind.Should().Be("rule");
        body.Rule.Should().NotBeNull();
        body.Rule!.TargetLayer.Should().Be("coaching_preferences");
        body.Rule.RuleText.Should().Be("Prefer morning long runs");
        body.Rule.Source.Should().Be(CoachRuleSources.Athlete);
        body.Episode.Should().BeNull();

        var list = await client.GetFromJsonAsync<List<RuleResponse>>(
            "/coach/memory/rules",
            JsonOptions);
        list!.Should().Contain(r => r.Id == body.Rule.Id);
    }

    [Fact]
    public async Task MemoryNote_EmptyText_ReturnsBadRequest()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        (await client.PostAsJsonAsync("/coach/memory/notes", new { text = "  " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MemoryNote_RetireUnretire_StillWorks()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var created = await client.PostAsJsonAsync("/coach/memory/notes", new
        {
            text = "Temporary note",
            date = "2026-10-01"
        });
        var body = await created.Content.ReadFromJsonAsync<MemoryNoteResponse>(JsonOptions);
        var id = body!.Episode!.Id;

        (await client.PostAsync($"/coach/memory/episodes/{id}/retire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var active = await client.GetFromJsonAsync<List<EpisodeResponse>>(
            "/coach/memory/episodes",
            JsonOptions);
        active!.Should().NotContain(e => e.Id == id);

        (await client.PostAsync($"/coach/memory/episodes/{id}/unretire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await client.GetFromJsonAsync<List<EpisodeResponse>>(
            "/coach/memory/episodes",
            JsonOptions);
        again!.Should().Contain(e => e.Id == id);
    }

    private sealed class EpisodeResponse
    {
        public string Id { get; set; } = "";
        public string Date { get; set; } = "";
        public string Category { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Source { get; set; } = "";
        public bool Retired { get; set; }
    }

    private sealed class RuleResponse
    {
        public string Id { get; set; } = "";
        public string TargetLayer { get; set; } = "";
        public string RuleText { get; set; } = "";
        public string Status { get; set; } = "";
        public List<string> Evidence { get; set; } = [];
        public string Source { get; set; } = "";
    }

    private sealed class MemoryNoteResponse
    {
        public string Kind { get; set; } = "";
        public EpisodeResponse? Episode { get; set; }
        public RuleResponse? Rule { get; set; }
    }
}
