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
public class CoachPlanDaysEndpointsTests : IClassFixture<CoachGenerateTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly DateOnly PlanWeekStart = new(2026, 10, 12);

    private readonly CoachGenerateTestFactory _factory;

    public CoachPlanDaysEndpointsTests(CoachGenerateTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClientAsync(bool optIn = true)
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

        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory.Decorated);
        if (optIn)
        {
            (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return client;
    }

    private async Task SeedCommittedWeekAsync(string? commitJson = null)
    {
        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var now = DateTime.UtcNow;
        db.CoachWeeks.Add(new CoachWeek
        {
            WeekStartDate = PlanWeekStart,
            Status = CoachWeekStatuses.Committed,
            CommitJson = commitJson ?? CoachWeeklyCommitSchemaTests.BuildCommitJson(weekStart: PlanWeekStart),
            PreflightId = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            PayloadHash = new string('a', 64),
            RedactedPayloadJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PlanDays_WhenHostDisabled_ReturnsNotFound()
    {
        await using var disabled = new TempoWebApplicationFactory();
        using var scope = disabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(disabled);

        var response = await client.GetAsync(
            "/coach/plan-days?from=2026-10-12&to=2026-10-18&timezoneOffsetMinutes=0");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PlanDays_WhenNotOptedIn_ReturnsNotFound()
    {
        var client = await AuthenticatedClientAsync(optIn: false);

        var response = await client.GetAsync(
            "/coach/plan-days?from=2026-10-12&to=2026-10-18&timezoneOffsetMinutes=0");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PlanDays_WhenNoCommittedPlan_ReturnsEmptyDays()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync(
            "/coach/plan-days?from=2026-10-12&to=2026-10-18&timezoneOffsetMinutes=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PlanDaysResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Days.Should().BeEmpty();
    }

    [Fact]
    public async Task PlanDays_ReturnsPlannedDayWithMatchedWorkoutsAndHints()
    {
        var client = await AuthenticatedClientAsync();
        await SeedCommittedWeekAsync();

        // Tue 2026-10-13 is rest in default BuildCommitJson; two runs → extra_on_rest.
        // Wed 2026-10-14 is easy 8000m; short run → short_distance.
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800);
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 13, 18, 0, 0, DateTimeKind.Utc),
                distanceM: 3000,
                durationS: 1200);
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc),
                distanceM: 4000,
                durationS: 1500);
        }

        var response = await client.GetAsync(
            "/coach/plan-days?from=2026-10-13&to=2026-10-14&timezoneOffsetMinutes=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PlanDaysResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Days.Should().HaveCount(2);

        var rest = body.Days.Single(d => d.Date == "2026-10-13");
        rest.DayType.Should().Be("rest");
        rest.Run.Should().BeNull();
        rest.Workouts.Should().HaveCount(2);
        rest.AdherenceHint.Should().NotBeNull();
        rest.AdherenceHint!.Code.Should().Be("extra_on_rest");

        var easy = body.Days.Single(d => d.Date == "2026-10-14");
        easy.DayType.Should().Be("easy");
        easy.Run.Should().NotBeNull();
        easy.Run!.DistanceM.Should().Be(8000);
        easy.Workouts.Should().HaveCount(1);
        easy.Workouts[0].DistanceM.Should().Be(4000);
        easy.AdherenceHint.Should().NotBeNull();
        easy.AdherenceHint!.Code.Should().Be("short_distance");
    }

    [Fact]
    public async Task PlanDays_MissedRun_ReturnsHintWithoutWorkouts()
    {
        var client = await AuthenticatedClientAsync();
        await SeedCommittedWeekAsync();

        var response = await client.GetAsync(
            "/coach/plan-days?from=2026-10-12&to=2026-10-12&timezoneOffsetMinutes=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PlanDaysResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Days.Should().ContainSingle();
        var day = body.Days[0];
        day.DayType.Should().Be("easy");
        day.Workouts.Should().BeEmpty();
        day.AdherenceHint.Should().NotBeNull();
        day.AdherenceHint!.Code.Should().Be("missed_run");
    }

    [Fact]
    public async Task PlanDays_MatchesWorkoutsByLocalCalendarDate()
    {
        var client = await AuthenticatedClientAsync();
        await SeedCommittedWeekAsync();

        // Offset -300 (UTC−5): 2026-10-14T02:00Z → local 2026-10-13 21:00 → Tue rest day.
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800);
        }

        var response = await client.GetAsync(
            "/coach/plan-days?from=2026-10-13&to=2026-10-14&timezoneOffsetMinutes=-300");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PlanDaysResponse>(JsonOptions);
        body.Should().NotBeNull();

        var tue = body!.Days.Single(d => d.Date == "2026-10-13");
        tue.Workouts.Should().ContainSingle();
        tue.AdherenceHint!.Code.Should().Be("extra_on_rest");

        var wed = body.Days.Single(d => d.Date == "2026-10-14");
        wed.Workouts.Should().BeEmpty();
    }

    [Fact]
    public async Task PlanDays_RejectsInvalidRange()
    {
        var client = await AuthenticatedClientAsync();

        var inverted = await client.GetAsync(
            "/coach/plan-days?from=2026-10-18&to=2026-10-12&timezoneOffsetMinutes=0");
        inverted.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var tooLong = await client.GetAsync(
            "/coach/plan-days?from=2026-10-01&to=2026-11-20&timezoneOffsetMinutes=0");
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed class PlanDaysResponse
    {
        public List<PlanDayResponse> Days { get; set; } = [];
    }

    private sealed class PlanDayResponse
    {
        public string Date { get; set; } = "";
        public string DayType { get; set; } = "";
        public PlanDayRunResponse? Run { get; set; }
        public List<PlanDayWorkoutResponse> Workouts { get; set; } = [];
        public PlanDayHintResponse? AdherenceHint { get; set; }
    }

    private sealed class PlanDayRunResponse
    {
        public string Type { get; set; } = "";
        public double DistanceM { get; set; }
        public string? Structure { get; set; }
        public string? PaceOrHr { get; set; }
        public Guid? ShoeId { get; set; }
        public string? ShoeName { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class PlanDayWorkoutResponse
    {
        public Guid Id { get; set; }
        public double DistanceM { get; set; }
        public int DurationS { get; set; }
        public byte? AvgHeartRateBpm { get; set; }
    }

    private sealed class PlanDayHintResponse
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Date { get; set; }
    }
}
