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
public class CoachCheckInEndpointsTests : IClassFixture<CoachEnabledTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachEnabledTestFactory _factory;

    public CoachCheckInEndpointsTests(CoachEnabledTestFactory factory)
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
    public async Task CheckIn_WhenHostDisabled_ReturnsNotFound()
    {
        using var disabled = new TempoWebApplicationFactory();
        using var scope = disabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(disabled);

        (await client.GetAsync("/coach/check-in")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync("/coach/check-in", new { overallFeel = "ok" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CheckIn_WhenNotOptedIn_ReturnsNotFound()
    {
        var client = await AuthenticatedClientAsync();

        (await client.GetAsync("/coach/check-in")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EmptyWeek_GetAndPut_Succeeds()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        var getResponse = await client.GetAsync("/coach/check-in?timezoneOffsetMinutes=0&referenceDate=2026-10-13");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var empty = await getResponse.Content.ReadFromJsonAsync<CheckInResponse>(JsonOptions);
        empty.Should().NotBeNull();
        empty!.WeekStartDate.Should().Be("2026-10-12");
        empty.WeekEndDate.Should().Be("2026-10-18");
        empty.Runs.Should().BeEmpty();
        empty.Saved.Should().BeFalse();
        empty.AdherenceHints.Should().BeEmpty();

        var putResponse = await client.PutAsJsonAsync("/coach/check-in", new
        {
            referenceDate = "2026-10-13",
            timezoneOffsetMinutes = 0,
            overallFeel = "tired but ok",
            niggles = (string?)null,
            lifeConstraints = "travel Thursday"
        });
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await putResponse.Content.ReadFromJsonAsync<CheckInResponse>(JsonOptions);
        saved.Should().NotBeNull();
        saved!.Saved.Should().BeTrue();
        saved.OverallFeel.Should().Be("tired but ok");
        saved.LifeConstraints.Should().Be("travel Thursday");
        saved.Runs.Should().BeEmpty();

        var getAgain = await client.GetAsync("/coach/check-in?timezoneOffsetMinutes=0&referenceDate=2026-10-13");
        var again = await getAgain.Content.ReadFromJsonAsync<CheckInResponse>(JsonOptions);
        again!.OverallFeel.Should().Be("tired but ok");
        again.Saved.Should().BeTrue();
    }

    [Fact]
    public async Task WeekWithWorkouts_AutofillsAndNoteOverlayDoesNotWriteBack()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        Guid workoutId;
        const string originalNotes = "Felt strong";
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc),
                distanceM: 10000,
                durationS: 3000,
                name: "Tempo");
            workout.Notes = originalNotes;
            workout.AvgHeartRateBpm = 155;
            workout.RunType = "Tempo";
            await db.SaveChangesAsync();
            workoutId = workout.Id;
        }

        var getResponse = await client.GetAsync("/coach/check-in?timezoneOffsetMinutes=0&referenceDate=2026-10-14");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await getResponse.Content.ReadFromJsonAsync<CheckInResponse>(JsonOptions);
        doc.Should().NotBeNull();
        doc!.Runs.Should().ContainSingle();
        var run = doc.Runs[0];
        run.WorkoutId.Should().Be(workoutId);
        run.DistanceM.Should().Be(10000);
        run.DurationS.Should().Be(3000);
        run.AvgHeartRateBpm.Should().Be(155);
        run.WorkoutNotes.Should().Be(originalNotes);
        run.Notes.Should().Be(originalNotes);
        run.NotesEdited.Should().BeFalse();

        var putResponse = await client.PutAsJsonAsync("/coach/check-in", new
        {
            referenceDate = "2026-10-14",
            timezoneOffsetMinutes = 0,
            overallFeel = "good",
            runNotes = new[]
            {
                new { workoutId, notes = "Shortened on purpose" }
            }
        });
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await putResponse.Content.ReadFromJsonAsync<CheckInResponse>(JsonOptions);
        after!.Runs.Should().ContainSingle();
        after.Runs[0].Notes.Should().Be("Shortened on purpose");
        after.Runs[0].WorkoutNotes.Should().Be(originalNotes);
        after.Runs[0].NotesEdited.Should().BeTrue();

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workoutId);
            workout.Notes.Should().Be(originalNotes);
        }
    }

    [Fact]
    public async Task Put_PersistsFeelScoreQuestionAndRunRpe()
    {
        var client = await AuthenticatedClientAsync();
        await OptInAsync(client);

        Guid workoutId;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc),
                distanceM: 8000,
                durationS: 2400,
                name: "Easy");
            workout.Rpe = 5;
            await db.SaveChangesAsync();
            workoutId = workout.Id;
        }

        var put = await client.PutAsJsonAsync("/coach/check-in", new
        {
            referenceDate = "2026-10-14",
            timezoneOffsetMinutes = 0,
            overallFeel = "solid",
            feelScore = 4,
            questionForCoach = "Should I add strides?",
            runNotes = new[]
            {
                new { workoutId, notes = (string?)null, rpe = 7 }
            }
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await put.Content.ReadFromJsonAsync<CheckInResponse>(JsonOptions);
        saved!.FeelScore.Should().Be(4);
        saved.QuestionForCoach.Should().Be("Should I add strides?");
        saved.Runs.Should().ContainSingle();
        saved.Runs[0].WorkoutRpe.Should().Be(5);
        saved.Runs[0].Rpe.Should().Be(7);
        saved.Runs[0].RpeEdited.Should().BeTrue();

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workoutId);
            workout.Rpe.Should().Be(5);
        }
    }

    private sealed class CheckInResponse
    {
        public string WeekStartDate { get; set; } = "";
        public string WeekEndDate { get; set; } = "";
        public string? OverallFeel { get; set; }
        public byte? FeelScore { get; set; }
        public string? LifeConstraints { get; set; }
        public string? QuestionForCoach { get; set; }
        public string? ClarifyingQuestion { get; set; }
        public bool Saved { get; set; }
        public List<CheckInRunResponse> Runs { get; set; } = [];
        public List<object> AdherenceHints { get; set; } = [];
    }

    private sealed class CheckInRunResponse
    {
        public Guid WorkoutId { get; set; }
        public double DistanceM { get; set; }
        public int DurationS { get; set; }
        public byte? AvgHeartRateBpm { get; set; }
        public string? WorkoutNotes { get; set; }
        public string? Notes { get; set; }
        public bool NotesEdited { get; set; }
        public byte? WorkoutRpe { get; set; }
        public byte? Rpe { get; set; }
        public bool RpeEdited { get; set; }
    }
}
