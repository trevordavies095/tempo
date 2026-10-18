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
public class CoachSetupEndpointsTests : IClassFixture<CoachProviderSettingsTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachProviderSettingsTestFactory _factory;

    public CoachSetupEndpointsTests(CoachProviderSettingsTestFactory factory)
    {
        _factory = factory;
        _factory.Fake.Reset();
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

    [Fact]
    public async Task HostDisabled_DeleteSetup_ReturnsNotFound()
    {
        using var disabled = new TempoWebApplicationFactory();
        using var scope = disabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(disabled);

        (await client.DeleteAsync("/settings/coach/setup")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteSetup_WipesPeelable_KeepsOptInAndProvider()
    {
        var client = await AuthenticatedClientAsync();

        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-keep-me" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync("/coach/onboarding", new
        {
            goal = new
            {
                type = "race",
                distance = "Half-Marathon",
                raceDate = "2027-01-09"
            },
            paces = new
            {
                easySecPerKm = 360,
                tempoSecPerKm = 310,
                thresholdSecPerKm = 280,
                intervalSecPerKm = 265
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
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync("/coach/constraints", new
        {
            kind = "blackout",
            startDate = "2026-12-04",
            endDate = "2026-12-06",
            note = "Travel"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var weekStart = new DateOnly(2026, 10, 12);
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var preflightId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var weekId = Guid.NewGuid();
            db.CoachPrivacyPreflights.Add(new CoachPrivacyPreflight
            {
                Id = preflightId,
                WeekStartDate = weekStart,
                TimezoneOffsetMinutes = 0,
                ReferenceDate = weekStart,
                RedactedPayloadJson = "{}",
                PrivacyReportJson = "{}",
                PayloadHash = new string('a', 64),
                AssembledAt = DateTime.UtcNow,
                ConsentedAt = DateTime.UtcNow
            });
            db.CoachCheckIns.Add(new CoachCheckIn
            {
                WeekStartDate = weekStart,
                TimezoneOffsetMinutes = 0,
                RunNotesJson = "[]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.CoachJobs.Add(new CoachJob
            {
                Id = jobId,
                Status = CoachJobStatuses.Queued,
                Kind = CoachJobKinds.Generate,
                WeekStartDate = weekStart,
                PreflightId = preflightId,
                CreatedAt = DateTime.UtcNow
            });
            db.CoachWeeks.Add(new CoachWeek
            {
                Id = weekId,
                WeekStartDate = weekStart,
                Status = CoachWeekStatuses.Draft,
                CommitJson = "{}",
                PreflightId = preflightId,
                JobId = jobId,
                PayloadHash = new string('b', 64),
                RedactedPayloadJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.CoachAdjustments.Add(new CoachAdjustment
            {
                WeekStartDate = weekStart,
                SourceCommittedWeekId = weekId,
                Reason = "test",
                LockedThroughDate = weekStart,
                Status = CoachAdjustmentStatuses.Started,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var delete = await client.DeleteAsync("/settings/coach/setup");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.CoachPreferences.SingleAsync()).OptedIn.Should().BeTrue();
            (await db.CoachProviderConnections.CountAsync()).Should().Be(1);
            (await db.CoachGoals.CountAsync()).Should().Be(0);
            (await db.CoachMemoryLayers.CountAsync()).Should().Be(0);
            (await db.CoachConstraints.CountAsync()).Should().Be(0);
            (await db.CoachCheckIns.CountAsync()).Should().Be(0);
            (await db.CoachPrivacyPreflights.CountAsync()).Should().Be(0);
            (await db.CoachJobs.CountAsync()).Should().Be(0);
            (await db.CoachWeeks.CountAsync()).Should().Be(0);
            (await db.CoachAdjustments.CountAsync()).Should().Be(0);
        }

        var settings = await client.GetFromJsonAsync<CoachSettingsResponse>("/settings/coach", JsonOptions);
        settings!.OptedIn.Should().BeTrue();
        settings.ProviderConnected.Should().BeTrue();

        var home = await client.GetFromJsonAsync<CoachHomeResponse>("/coach", JsonOptions);
        home.Should().NotBeNull();
        home!.OnboardingCompleted.Should().BeFalse();
        home.NextSteps.Should().Contain(s => s.Contains("onboarding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteSetup_InterruptsActiveJobThenWipes()
    {
        var client = await AuthenticatedClientAsync();
        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var weekStart = new DateOnly(2026, 10, 12);
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var preflightId = Guid.NewGuid();
            db.CoachPrivacyPreflights.Add(new CoachPrivacyPreflight
            {
                Id = preflightId,
                WeekStartDate = weekStart,
                TimezoneOffsetMinutes = 0,
                ReferenceDate = weekStart,
                RedactedPayloadJson = "{}",
                PrivacyReportJson = "{}",
                PayloadHash = new string('a', 64),
                AssembledAt = DateTime.UtcNow,
                ConsentedAt = DateTime.UtcNow
            });
            db.CoachJobs.Add(new CoachJob
            {
                Status = CoachJobStatuses.Running,
                Kind = CoachJobKinds.Generate,
                WeekStartDate = weekStart,
                PreflightId = preflightId,
                StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        (await client.DeleteAsync("/settings/coach/setup")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var assertScope = _factory.Decorated.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await assertDb.CoachJobs.CountAsync()).Should().Be(0);
        (await assertDb.CoachPrivacyPreflights.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAll_WipesIncludingProviderAndPreferences_CoreIntact()
    {
        var client = await AuthenticatedClientAsync();

        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-wipe-me" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync("/coach/onboarding", new
        {
            goal = new
            {
                type = "race",
                distance = "Half-Marathon",
                raceDate = "2027-01-09"
            },
            paces = new
            {
                easySecPerKm = 360,
                tempoSecPerKm = 310,
                thresholdSecPerKm = 280,
                intervalSecPerKm = 265
            }
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync("/coach/memory/notes", new
        {
            text = "Keep mornings easy",
            date = "2026-09-01"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        Guid shoeId;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus");
            shoeId = shoe.Id;
            if (await db.UserSettings.CountAsync() == 0)
            {
                await TestDataSeeder.SeedUserSettingsAsync(db);
            }
        }

        (await client.DeleteAsync("/settings/coach")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.CoachPreferences.CountAsync()).Should().Be(0);
            (await db.CoachProviderConnections.CountAsync()).Should().Be(0);
            (await db.CoachGoals.CountAsync()).Should().Be(0);
            (await db.CoachEpisodes.CountAsync()).Should().Be(0);
            (await db.CoachRules.CountAsync()).Should().Be(0);
            (await db.CoachMemoryLayers.CountAsync()).Should().Be(0);
            (await db.Shoes.CountAsync(s => s.Id == shoeId)).Should().Be(1);
            (await db.UserSettings.CountAsync()).Should().BeGreaterThan(0);
        }

        var settings = await client.GetFromJsonAsync<CoachSettingsResponse>("/settings/coach", JsonOptions);
        settings!.OptedIn.Should().BeFalse();
        settings.ProviderConnected.Should().BeFalse();

        (await client.GetAsync("/coach")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OptOut_DoesNotWipeCoachData()
    {
        var client = await AuthenticatedClientAsync();
        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-keep" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/coach/onboarding", new
        {
            goal = new
            {
                type = "race",
                distance = "Half-Marathon",
                raceDate = "2027-01-09"
            },
            paces = new
            {
                easySecPerKm = 360,
                tempoSecPerKm = 310,
                thresholdSecPerKm = 280,
                intervalSecPerKm = 265
            }
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsync("/settings/coach/opt-out", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.CoachPreferences.SingleAsync()).OptedIn.Should().BeFalse();
            (await db.CoachProviderConnections.CountAsync()).Should().Be(1);
            (await db.CoachGoals.CountAsync()).Should().Be(1);
            (await db.CoachMemoryLayers.CountAsync()).Should().BeGreaterThan(0);
        }
    }

    private class CoachSettingsResponse
    {
        public bool Available { get; set; }
        public bool OptedIn { get; set; }
        public bool ProviderConnected { get; set; }
    }

    private class CoachHomeResponse
    {
        public List<string> NextSteps { get; set; } = new();
        public bool OnboardingCompleted { get; set; }
    }
}
