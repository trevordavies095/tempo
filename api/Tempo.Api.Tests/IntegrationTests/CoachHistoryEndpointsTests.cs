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
public class CoachHistoryEndpointsTests : IClassFixture<CoachGenerateTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly DateOnly PlanWeekStart = new(2026, 10, 12);
    private static readonly DateOnly CheckInWeekStart = new(2026, 10, 5);
    private static readonly DateOnly OlderPlanWeekStart = new(2026, 10, 5);

    private readonly CoachGenerateTestFactory _factory;

    public CoachHistoryEndpointsTests(CoachGenerateTestFactory factory)
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

    private async Task<(Guid CommittedId, Guid ArchivedId, Guid DraftId)> SeedWeeksAsync()
    {
        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var now = DateTime.UtcNow;

        var adjustJobId = Guid.NewGuid();
        var plainJobId = Guid.NewGuid();
        var draftJobId = Guid.NewGuid();

        db.CoachJobs.AddRange(
            new CoachJob
            {
                Id = adjustJobId,
                Status = CoachJobStatuses.Ready,
                Kind = CoachJobKinds.Generate,
                WeekStartDate = PlanWeekStart,
                PreflightId = Guid.NewGuid(),
                AdjustmentId = Guid.NewGuid(),
                CreatedAt = now
            },
            new CoachJob
            {
                Id = plainJobId,
                Status = CoachJobStatuses.Ready,
                Kind = CoachJobKinds.Generate,
                WeekStartDate = OlderPlanWeekStart,
                PreflightId = Guid.NewGuid(),
                CreatedAt = now.AddDays(-7)
            },
            new CoachJob
            {
                Id = draftJobId,
                Status = CoachJobStatuses.Ready,
                Kind = CoachJobKinds.Generate,
                WeekStartDate = PlanWeekStart.AddDays(7),
                PreflightId = Guid.NewGuid(),
                CreatedAt = now
            });

        var committed = new CoachWeek
        {
            WeekStartDate = PlanWeekStart,
            Status = CoachWeekStatuses.Committed,
            CommitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(weekStart: PlanWeekStart),
            PreflightId = Guid.NewGuid(),
            JobId = adjustJobId,
            PayloadHash = new string('c', 64),
            RedactedPayloadJson = """{"recentTraining":{"weeks":[]},"selectedMemory":{"episodeIds":[],"ruleIds":[]}}""",
            ApplyRecordJson = """{"appliedAt":"2026-10-12T12:00:00Z","safetyAcknowledgments":[],"acceptedLayer1PatchIndexes":[],"acceptedEpisodeIndexes":[],"acceptedRuleProposalIndexes":[],"archiveNotesAsRecap":false,"appliedLayer1Paths":[],"appliedEpisodeIds":[],"appliedRuleIds":[],"recapEpisodeId":null}""",
            CreatedAt = now,
            UpdatedAt = now
        };
        var archived = new CoachWeek
        {
            WeekStartDate = PlanWeekStart,
            Status = CoachWeekStatuses.Archived,
            CommitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(weekStart: PlanWeekStart),
            PreflightId = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            PayloadHash = new string('a', 64),
            RedactedPayloadJson = "{}",
            CreatedAt = now.AddHours(-2),
            UpdatedAt = now.AddHours(-1)
        };
        var older = new CoachWeek
        {
            WeekStartDate = OlderPlanWeekStart,
            Status = CoachWeekStatuses.Committed,
            CommitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(weekStart: OlderPlanWeekStart),
            PreflightId = Guid.NewGuid(),
            JobId = plainJobId,
            PayloadHash = new string('b', 64),
            RedactedPayloadJson = "{}",
            CreatedAt = now.AddDays(-7),
            UpdatedAt = now.AddDays(-7)
        };
        var draft = new CoachWeek
        {
            WeekStartDate = PlanWeekStart.AddDays(7),
            Status = CoachWeekStatuses.Draft,
            CommitJson = CoachWeeklyCommitSchemaTests.BuildCommitJson(weekStart: PlanWeekStart.AddDays(7)),
            PreflightId = Guid.NewGuid(),
            JobId = draftJobId,
            PayloadHash = new string('d', 64),
            RedactedPayloadJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.CoachWeeks.AddRange(committed, archived, older, draft);

        db.CoachCheckIns.Add(new CoachCheckIn
        {
            WeekStartDate = CheckInWeekStart,
            TimezoneOffsetMinutes = 0,
            FeelScore = 4,
            RunNotesJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
        return (committed.Id, archived.Id, draft.Id);
    }

    [Fact]
    public async Task Weeks_WhenHostDisabled_ReturnsNotFound()
    {
        await using var disabled = new TempoWebApplicationFactory();
        using var scope = disabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(disabled);

        (await client.GetAsync("/coach/weeks?timezoneOffsetMinutes=0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Weeks_WhenNotOptedIn_ReturnsNotFound()
    {
        var client = await AuthenticatedClientAsync(optIn: false);
        (await client.GetAsync("/coach/weeks?timezoneOffsetMinutes=0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Weeks_ListExcludesDrafts_IncludesCommittedAndArchived_NewestFirst()
    {
        var client = await AuthenticatedClientAsync();
        await SeedWeeksAsync();

        var response = await client.GetAsync("/coach/weeks?page=1&pageSize=20&timezoneOffsetMinutes=0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WeeksListResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.TotalCount.Should().Be(3);
        body.Items.Should().HaveCount(3);
        body.Items.Select(i => i.Status).Should().NotContain(CoachWeekStatuses.Draft);

        // Newest Monday first; for same Monday, committed (later UpdatedAt) before archived.
        body.Items[0].WeekStartDate.Should().Be("2026-10-12");
        body.Items[0].Status.Should().Be(CoachWeekStatuses.Committed);
        body.Items[0].IsAdjustment.Should().BeTrue();
        body.Items[0].FeelScore.Should().Be(4);
        body.Items[1].WeekStartDate.Should().Be("2026-10-12");
        body.Items[1].Status.Should().Be(CoachWeekStatuses.Archived);
        body.Items[2].WeekStartDate.Should().Be("2026-10-05");
        body.Items[2].FeelScore.Should().BeNull();
    }

    [Fact]
    public async Task Weeks_PagingWorks()
    {
        var client = await AuthenticatedClientAsync();
        await SeedWeeksAsync();

        var page1 = await client.GetAsync("/coach/weeks?page=1&pageSize=2&timezoneOffsetMinutes=0");
        page1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await page1.Content.ReadFromJsonAsync<WeeksListResponse>(JsonOptions);
        body1!.Items.Should().HaveCount(2);
        body1.TotalCount.Should().Be(3);
        body1.TotalPages.Should().Be(2);

        var page2 = await client.GetAsync("/coach/weeks?page=2&pageSize=2&timezoneOffsetMinutes=0");
        var body2 = await page2.Content.ReadFromJsonAsync<WeeksListResponse>(JsonOptions);
        body2!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Weeks_PlannedAndActualDistanceSums()
    {
        var client = await AuthenticatedClientAsync();
        var (committedId, _, _) = await SeedWeeksAsync();

        // Default BuildCommitJson: Mon easy 8000, Tue rest, Wed easy 8000, Thu quality 8000,
        // Fri rest, Sat easy 8000, Sun long 16000 → planned 48000.
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc),
                distanceM: 7500,
                durationS: 2400);
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
                distanceM: 15000,
                durationS: 5400);
        }

        var response = await client.GetAsync("/coach/weeks?timezoneOffsetMinutes=0");
        var body = await response.Content.ReadFromJsonAsync<WeeksListResponse>(JsonOptions);
        var committed = body!.Items.Single(i => i.Id == committedId);
        committed.PlannedDistanceM.Should().Be(48000);
        committed.ActualDistanceM.Should().Be(22500);
    }

    [Fact]
    public async Task WeekById_ReturnsPlanDaysAndReceipt_404ForDraftAndUnknown()
    {
        var client = await AuthenticatedClientAsync();
        var (committedId, _, draftId) = await SeedWeeksAsync();

        var ok = await client.GetAsync($"/coach/weeks/{committedId}?timezoneOffsetMinutes=0");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await ok.Content.ReadFromJsonAsync<WeekDetailResponse>(JsonOptions);
        detail.Should().NotBeNull();
        detail!.Id.Should().Be(committedId);
        detail.Status.Should().Be(CoachWeekStatuses.Committed);
        detail.Days.Should().HaveCount(7);
        detail.PrivacyReceipt.Should().NotBeNull();
        detail.PrivacyReceipt!.PayloadHash.Should().Be(new string('c', 64));
        detail.PayloadHash.Should().Be(detail.PrivacyReceipt.PayloadHash);
        detail.ApplyRecord.Should().NotBeNull();

        (await client.GetAsync($"/coach/weeks/{draftId}?timezoneOffsetMinutes=0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/coach/weeks/{Guid.NewGuid()}?timezoneOffsetMinutes=0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class WeeksListResponse
    {
        public List<WeekListItemResponse> Items { get; set; } = [];
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class WeekListItemResponse
    {
        public Guid Id { get; set; }
        public string WeekStartDate { get; set; } = "";
        public string WeekEndDate { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsAdjustment { get; set; }
        public byte? FeelScore { get; set; }
        public double PlannedDistanceM { get; set; }
        public double ActualDistanceM { get; set; }
    }

    private sealed class WeekDetailResponse
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public string PayloadHash { get; set; } = "";
        public JsonElement? ApplyRecord { get; set; }
        public List<PlanDayResponse> Days { get; set; } = [];
        public ReceiptResponse? PrivacyReceipt { get; set; }
    }

    private sealed class PlanDayResponse
    {
        public string Date { get; set; } = "";
        public string DayType { get; set; } = "";
    }

    private sealed class ReceiptResponse
    {
        public string PayloadHash { get; set; } = "";
    }
}
