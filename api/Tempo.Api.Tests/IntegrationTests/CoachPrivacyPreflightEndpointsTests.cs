using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
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
    public async Task AssembleEmptyWeek_ThenConsent()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var assemble = await client.PostAsync(
            "/coach/privacy/preflight?timezoneOffsetMinutes=0&referenceDate=2026-10-13",
            null);
        assemble.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await assemble.Content.ReadFromJsonAsync<PreflightResponse>(JsonOptions);
        doc.Should().NotBeNull();
        doc!.WeekStartDate.Should().Be("2026-10-12");
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
    }

    [Fact]
    public async Task Assemble_WithNamedWorkout_DoesNotLeakNameInPayload()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

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

    private sealed class PreflightResponse
    {
        public Guid Id { get; set; }
        public string WeekStartDate { get; set; } = "";
        public bool Consented { get; set; }
        public DateTime? ConsentedAt { get; set; }
        public object? RedactedPayload { get; set; }
        public ReportDto Report { get; set; } = new();
    }

    private sealed class ReportDto
    {
        public List<ReportItemDto> Included { get; set; } = [];
        public List<ReportItemDto> Stripped { get; set; } = [];
    }

    private sealed class ReportItemDto
    {
        public string Category { get; set; } = "";
        public string Detail { get; set; } = "";
    }
}
