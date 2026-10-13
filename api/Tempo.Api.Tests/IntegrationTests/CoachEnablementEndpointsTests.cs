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
public class CoachEnablementEndpointsTests : IClassFixture<TempoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TempoWebApplicationFactory _factory;

    public CoachEnablementEndpointsTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        return await TestHttpClientFactory.CreateAuthenticatedClientAsync(factory);
    }

    private static WebApplicationFactory<Program> CreateCoachEnabledFactory()
    {
        return new TempoWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(s => s.ServiceType == typeof(CoachConfig));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddSingleton(new CoachConfig { Enabled = true });
                });
            });
    }

    [Fact]
    public async Task GetSettings_WhenHostDisabled_ReturnsUnavailable()
    {
        var client = await AuthenticatedClientAsync(_factory);

        var response = await client.GetAsync("/settings/coach");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CoachSettingsResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Available.Should().BeFalse();
        body.OptedIn.Should().BeFalse();
    }

    [Fact]
    public async Task CoachHomeAndOptIn_WhenHostDisabled_ReturnNotFound()
    {
        var client = await AuthenticatedClientAsync(_factory);

        var home = await client.GetAsync("/coach");
        var optIn = await client.PostAsync("/settings/coach/opt-in", null);
        var optOut = await client.PostAsync("/settings/coach/opt-out", null);

        home.StatusCode.Should().Be(HttpStatusCode.NotFound);
        optIn.StatusCode.Should().Be(HttpStatusCode.NotFound);
        optOut.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSettings_WhenHostEnabled_NotOptedIn_ReturnsAvailableFalseOptIn()
    {
        using var factory = CreateCoachEnabledFactory();
        var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/settings/coach");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CoachSettingsResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Available.Should().BeTrue();
        body.OptedIn.Should().BeFalse();
    }

    [Fact]
    public async Task GetCoachHome_WhenHostEnabledNotOptedIn_ReturnsNotFound()
    {
        using var factory = CreateCoachEnabledFactory();
        var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/coach");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OptIn_ThenHome_ReturnsShell_OptOut_GatesAgain_KeepsRow()
    {
        using var factory = CreateCoachEnabledFactory();
        var client = await AuthenticatedClientAsync(factory);

        var optIn = await client.PostAsync("/settings/coach/opt-in", null);
        optIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var optedInBody = await optIn.Content.ReadFromJsonAsync<CoachSettingsResponse>(JsonOptions);
        optedInBody.Should().NotBeNull();
        optedInBody!.Available.Should().BeTrue();
        optedInBody.OptedIn.Should().BeTrue();

        var home = await client.GetAsync("/coach");
        home.StatusCode.Should().Be(HttpStatusCode.OK);
        var shell = await home.Content.ReadFromJsonAsync<CoachHomeResponse>(JsonOptions);
        shell.Should().NotBeNull();
        shell!.Title.Should().Be("AI Coach");
        shell.Disclaimer.Should().Contain("not a doctor");
        shell.NextSteps.Should().NotBeEmpty();

        var optOut = await client.PostAsync("/settings/coach/opt-out", null);
        optOut.StatusCode.Should().Be(HttpStatusCode.OK);
        var optedOutBody = await optOut.Content.ReadFromJsonAsync<CoachSettingsResponse>(JsonOptions);
        optedOutBody.Should().NotBeNull();
        optedOutBody!.Available.Should().BeTrue();
        optedOutBody.OptedIn.Should().BeFalse();

        var homeAfter = await client.GetAsync("/coach");
        homeAfter.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var row = await db.CoachPreferences.SingleAsync();
            row.OptedIn.Should().BeFalse();
        }
    }

    [Fact]
    public async Task OptIn_IsIdempotent()
    {
        using var factory = CreateCoachEnabledFactory();
        var client = await AuthenticatedClientAsync(factory);

        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CoachPreferences.CountAsync()).Should().Be(1);
    }

    private class CoachSettingsResponse
    {
        public bool Available { get; set; }
        public bool OptedIn { get; set; }
    }

    private class CoachHomeResponse
    {
        public string Title { get; set; } = string.Empty;
        public string Disclaimer { get; set; } = string.Empty;
        public List<string> NextSteps { get; set; } = new();
    }
}
