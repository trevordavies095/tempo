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
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class CoachConstraintEndpointsTests : IClassFixture<CoachEnabledTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachEnabledTestFactory _factory;

    public CoachConstraintEndpointsTests(CoachEnabledTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClientAsync(bool tempoOnboardingCompleted = true)
    {
        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "testuser");
        if (user == null)
        {
            user = await TestDataSeeder.SeedUserAsync(db, "testuser");
        }

        user.OnboardingCompleted = tempoOnboardingCompleted;
        await db.SaveChangesAsync();

        return await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory.Decorated);
    }

    private static async Task OptInAsync(HttpClient client)
    {
        var response = await client.PostAsync("/settings/coach/opt-in", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Constraints_WhenHostDisabled_ReturnNotFound()
    {
        using var disabled = new TempoWebApplicationFactory();
        using var scope = disabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(disabled);

        (await client.GetAsync("/coach/constraints")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/coach/constraints", new
        {
            kind = "blackout",
            startDate = "2026-12-01"
        })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Constraints_WhenNotOptedIn_ReturnNotFound()
    {
        var client = await AuthenticatedClientAsync();

        (await client.GetAsync("/coach/constraints")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_SecondaryRace_DefaultsIntentTuneUp()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.PostAsJsonAsync("/coach/constraints", new
        {
            kind = "secondary_race",
            startDate = "2026-11-27",
            distance = "5K"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ConstraintResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Kind.Should().Be("secondary_race");
        body.Intent.Should().Be("tune_up");
        body.Distance.Should().Be("5K");
        body.DistanceM.Should().Be(5000);
        body.IsRetired.Should().BeFalse();
    }

    [Fact]
    public async Task Create_BlackoutAndStanding_Succeed()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var blackout = await client.PostAsJsonAsync("/coach/constraints", new
        {
            kind = "blackout",
            startDate = "2026-12-04",
            endDate = "2026-12-06",
            note = "Wedding travel"
        });
        blackout.StatusCode.Should().Be(HttpStatusCode.OK);
        var blackoutBody = await blackout.Content.ReadFromJsonAsync<ConstraintResponse>(JsonOptions);
        blackoutBody!.StartDate.Should().Be("2026-12-04");
        blackoutBody.EndDate.Should().Be("2026-12-06");

        var standing = await client.PostAsJsonAsync("/coach/constraints", new
        {
            kind = "standing_schedule",
            preset = "long_day",
            weekdays = new[] { 7 },
            note = "Sunday long"
        });
        standing.StatusCode.Should().Be(HttpStatusCode.OK);
        var standingBody = await standing.Content.ReadFromJsonAsync<ConstraintResponse>(JsonOptions);
        standingBody!.Preset.Should().Be("long_day");
        standingBody.Weekdays.Should().Equal(7);
    }

    [Fact]
    public async Task Create_UnknownStandingPreset_ReturnsBadRequest()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var response = await client.PostAsJsonAsync("/coach/constraints", new
        {
            kind = "standing_schedule",
            preset = "free_text_dsl",
            weekdays = new[] { 1 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("preset");
    }

    [Fact]
    public async Task Update_And_Retire_ListFilters()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var created = await client.PostAsJsonAsync("/coach/constraints", new
        {
            kind = "standing_schedule",
            preset = "no_run_on",
            weekdays = new[] { 1, 3 }
        });
        var row = await created.Content.ReadFromJsonAsync<ConstraintResponse>(JsonOptions);

        var updated = await client.PutAsJsonAsync($"/coach/constraints/{row!.Id}", new
        {
            kind = "standing_schedule",
            preset = "no_quality_on",
            weekdays = new[] { 2, 4 }
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedBody = await updated.Content.ReadFromJsonAsync<ConstraintResponse>(JsonOptions);
        updatedBody!.Preset.Should().Be("no_quality_on");
        updatedBody.Weekdays.Should().Equal(2, 4);

        var retire = await client.PostAsync($"/coach/constraints/{row.Id}/retire", null);
        retire.StatusCode.Should().Be(HttpStatusCode.OK);

        var active = await client.GetFromJsonAsync<List<ConstraintResponse>>("/coach/constraints?status=active", JsonOptions);
        active.Should().BeEmpty();

        var retired = await client.GetFromJsonAsync<List<ConstraintResponse>>("/coach/constraints?status=retired", JsonOptions);
        retired.Should().ContainSingle(c => c.Id == row.Id && c.IsRetired);

        var all = await client.GetFromJsonAsync<List<ConstraintResponse>>("/coach/constraints?status=all", JsonOptions);
        all.Should().ContainSingle();

        (await client.PutAsJsonAsync($"/coach/constraints/{row.Id}", new
        {
            kind = "standing_schedule",
            preset = "long_day",
            weekdays = new[] { 6 }
        })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.PostAsync($"/coach/constraints/{row.Id}/retire", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private class ConstraintResponse
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? Distance { get; set; }
        public double? DistanceM { get; set; }
        public string? Intent { get; set; }
        public string? Preset { get; set; }
        public List<int>? Weekdays { get; set; }
        public string? Note { get; set; }
        public bool IsRetired { get; set; }
    }
}

/// <summary>
/// Shared Coach-enabled factory so constraint tests reuse one DB clone.
/// </summary>
public sealed class CoachEnabledTestFactory : IDisposable
{
    private readonly TempoWebApplicationFactory _inner = new();

    public WebApplicationFactory<Program> Decorated { get; }

    public CoachEnabledTestFactory()
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
            });
        });
    }

    public void Dispose()
    {
        Decorated.Dispose();
        _inner.Dispose();
    }
}
