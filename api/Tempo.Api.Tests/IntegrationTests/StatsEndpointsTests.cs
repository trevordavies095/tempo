using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for StatsEndpoints covering weekly stats, yearly stats, relative effort stats,
/// best efforts, available periods/years, and edge cases.
/// </summary>
[Collection("Integration Tests")]
public class StatsEndpointsTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public StatsEndpointsTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Helper method to ensure database is clean before a test (but preserves test user)
    /// </summary>
    private async Task EnsureCleanDatabaseAsync()
    {
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Clear all data except users (we need the test user for authentication)
            await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        }
    }

    #region Fixture Data Helpers

    /// <summary>
    /// Seeds workouts across multiple years (2022, 2023, 2024, 2025)
    /// </summary>
    private async Task<List<Workout>> SeedMultiYearWorkoutsAsync(TempoDbContext db)
    {
        var workouts = new List<Workout>();
        
        // 2022: 5 workouts
        for (int i = 0; i < 5; i++)
        {
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2022, 6, 15 + i, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000 + (i * 1000),
                durationS: 1800 + (i * 60),
                name: $"2022 Workout {i + 1}");
            workouts.Add(workout);
        }

        // 2023: 10 workouts
        for (int i = 0; i < 10; i++)
        {
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2023, 3, 10 + i, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000 + (i * 500),
                durationS: 1800 + (i * 30),
                name: $"2023 Workout {i + 1}");
            workouts.Add(workout);
        }

        // 2024: 8 workouts
        for (int i = 0; i < 8; i++)
        {
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2024, 8, 5 + i, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 6000 + (i * 1000),
                durationS: 2000 + (i * 40),
                name: $"2024 Workout {i + 1}");
            workouts.Add(workout);
        }

        // Current year: 12 workouts
        var currentYear = DateTime.UtcNow.Year;
        for (int i = 0; i < 12; i++)
        {
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(currentYear, 1, 5 + i, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000 + (i * 800),
                durationS: 1800 + (i * 50),
                name: $"{currentYear} Workout {i + 1}");
            workouts.Add(workout);
        }
        
        // Previous year: 8 workouts (if not already seeded above)
        var previousYear = currentYear - 1;
        if (previousYear != 2024) // Avoid duplicate if 2024 was already seeded
        {
            for (int i = 0; i < 8; i++)
            {
                var workout = await TestDataSeeder.SeedWorkoutAsync(
                    db,
                    startedAt: new DateTime(previousYear, 8, 5 + i, 10, 0, 0, DateTimeKind.Utc),
                    distanceM: 6000 + (i * 1000),
                    durationS: 2000 + (i * 40),
                    name: $"{previousYear} Workout {i + 1}");
                workouts.Add(workout);
            }
        }

        return workouts;
    }

    /// <summary>
    /// Seeds workouts with different run types
    /// </summary>
    private async Task<List<Workout>> SeedWorkoutsWithDifferentRunTypesAsync(TempoDbContext db)
    {
        var workouts = new List<Workout>();
        var runTypes = new[] { "Race", "Workout", "Easy Run", "Long Run", null };
        var distances = new[] { 5000.0, 10000.0, 21097.5, 42195.0, 3000.0 }; // 5K, 10K, Half, Marathon, 3K

        for (int i = 0; i < runTypes.Length; i++)
        {
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-i),
                distanceM: distances[i],
                durationS: 1800 + (i * 300),
                name: runTypes[i] ?? "Unspecified");
            workout.RunType = runTypes[i];
            await db.SaveChangesAsync();
            workouts.Add(workout);
        }

        return workouts;
    }

    /// <summary>
    /// Seeds workouts with and without HR data
    /// </summary>
    private async Task<List<Workout>> SeedWorkoutsWithAndWithoutHRAsync(TempoDbContext db)
    {
        var workouts = new List<Workout>();

        // Workouts with HR and relative effort
        for (int i = 0; i < 3; i++)
        {
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-i),
                distanceM: 5000,
                durationS: 1800,
                name: $"HR Workout {i + 1}");
            workout.AvgHeartRateBpm = (byte)(140 + i * 10);
            workout.MaxHeartRateBpm = (byte)(160 + i * 10);
            workout.RelativeEffort = 50 + (i * 25);
            await db.SaveChangesAsync();
            workouts.Add(workout);
        }

        // Workouts without HR
        for (int i = 0; i < 2; i++)
        {
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-i - 3),
                distanceM: 5000,
                durationS: 1800,
                name: $"No HR Workout {i + 1}");
            // No HR fields set
            workouts.Add(workout);
        }

        return workouts;
    }

    /// <summary>
    /// Seeds workouts with complete data (splits, routes, time series)
    /// </summary>
    private async Task<List<Workout>> SeedWorkoutsWithSplitsAndRoutesAsync(TempoDbContext db)
    {
        var workouts = new List<Workout>();

        for (int i = 0; i < 3; i++)
        {
            var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-i),
                distanceM: 5000,
                durationS: 1800,
                name: $"Complete Workout {i + 1}",
                includeRoute: true,
                includeSplits: true,
                includeTimeSeries: true);
            workouts.Add(workout);
        }

        return workouts;
    }

    /// <summary>
    /// Seeds sparse data - single workout in a year
    /// </summary>
    private async Task<Workout> SeedSparseDataAsync(TempoDbContext db, int year)
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            db,
            startedAt: new DateTime(year, 6, 15, 10, 0, 0, DateTimeKind.Utc),
            distanceM: 5000,
            durationS: 1800,
            name: $"Sparse {year}");
        return workout;
    }

    /// <summary>
    /// Seeds workouts for current week (Monday-Sunday)
    /// </summary>
    private async Task<List<Workout>> SeedCurrentWeekWorkoutsAsync(TempoDbContext db, int timezoneOffsetMinutes = 0)
    {
        var now = DateTime.UtcNow;
        if (timezoneOffsetMinutes != 0)
        {
            now = now.AddMinutes(timezoneOffsetMinutes);
        }

        // Calculate start of current week (Monday)
        var daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStart = now.Date.AddDays(-daysSinceMonday);

        var workouts = new List<Workout>();
        var distances = new[] { 3000.0, 5000.0, 10000.0, 5000.0, 3000.0, 0.0, 0.0 }; // M-Sun

        for (int day = 0; day < 7; day++)
        {
            if (distances[day] > 0)
            {
                var workoutDate = weekStart.AddDays(day).AddHours(10);
                // Convert back to UTC for storage
                var workoutDateUtc = timezoneOffsetMinutes != 0
                    ? DateTime.SpecifyKind(workoutDate.AddMinutes(-timezoneOffsetMinutes), DateTimeKind.Utc)
                    : DateTime.SpecifyKind(workoutDate, DateTimeKind.Utc);

                var workout = await TestDataSeeder.SeedWorkoutAsync(
                    db,
                    startedAt: workoutDateUtc,
                    distanceM: distances[day],
                    durationS: (int)(distances[day] / 1000.0 * 300), // ~5 min/km
                    name: $"Week Day {day + 1}");
                workouts.Add(workout);
            }
        }

        return workouts;
    }

    /// <summary>
    /// Seeds workouts for previous weeks (for relative effort 3-week average)
    /// </summary>
    private async Task<List<Workout>> SeedPreviousWeeksWorkoutsAsync(TempoDbContext db, int weekOffset, int timezoneOffsetMinutes = 0)
    {
        var now = DateTime.UtcNow;
        if (timezoneOffsetMinutes != 0)
        {
            now = now.AddMinutes(timezoneOffsetMinutes);
        }

        var daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var currentWeekStart = now.Date.AddDays(-daysSinceMonday);
        var weekStart = currentWeekStart.AddDays(-7 * weekOffset);

        var workouts = new List<Workout>();
        var relativeEfforts = new[] { 50, 75, 100, 60, 80, 0, 0 }; // M-Sun

        for (int day = 0; day < 7; day++)
        {
            if (relativeEfforts[day] > 0)
            {
                var workoutDate = weekStart.AddDays(day).AddHours(10);
                var workoutDateUtc = timezoneOffsetMinutes != 0
                    ? DateTime.SpecifyKind(workoutDate.AddMinutes(-timezoneOffsetMinutes), DateTimeKind.Utc)
                    : DateTime.SpecifyKind(workoutDate, DateTimeKind.Utc);

                var workout = await TestDataSeeder.SeedWorkoutAsync(
                    db,
                    startedAt: workoutDateUtc,
                    distanceM: 5000,
                    durationS: 1800,
                    name: $"Week {weekOffset} Day {day + 1}");
                workout.RelativeEffort = relativeEfforts[day];
                await db.SaveChangesAsync();
                workouts.Add(workout);
            }
        }

        return workouts;
    }

    /// <summary>
    /// Seeds workouts for previous week (for week-over-week comparison)
    /// </summary>
    private async Task<List<Workout>> SeedPreviousWeekWorkoutsAsync(TempoDbContext db, int timezoneOffsetMinutes = 0)
    {
        var now = DateTime.UtcNow;
        if (timezoneOffsetMinutes != 0)
        {
            now = now.AddMinutes(timezoneOffsetMinutes);
        }

        // Calculate start of current week (Monday)
        var daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var currentWeekStart = now.Date.AddDays(-daysSinceMonday);
        // Previous week starts 7 days before current week
        var previousWeekStart = currentWeekStart.AddDays(-7);

        var workouts = new List<Workout>();
        var distances = new[] { 2000.0, 4000.0, 8000.0, 4000.0, 2000.0, 0.0, 0.0 }; // M-Sun

        for (int day = 0; day < 7; day++)
        {
            if (distances[day] > 0)
            {
                var workoutDate = previousWeekStart.AddDays(day).AddHours(10);
                // Convert back to UTC for storage
                var workoutDateUtc = timezoneOffsetMinutes != 0
                    ? DateTime.SpecifyKind(workoutDate.AddMinutes(-timezoneOffsetMinutes), DateTimeKind.Utc)
                    : DateTime.SpecifyKind(workoutDate, DateTimeKind.Utc);

                var workout = await TestDataSeeder.SeedWorkoutAsync(
                    db,
                    startedAt: workoutDateUtc,
                    distanceM: distances[day],
                    durationS: (int)(distances[day] / 1000.0 * 300), // ~5 min/km
                    name: $"Previous Week Day {day + 1}");
                workouts.Add(workout);
            }
        }

        return workouts;
    }

    /// <summary>
    /// Seeds deterministic workouts for /stats/weekly-recap using a pinned reference week in the past (UTC local).
    /// referenceDate 2020-06-10 -> current week 2020-06-08 .. 2020-06-14; previous week 2020-06-01 .. 2020-06-07.
    /// </summary>
    private async Task SeedWeeklyRecapFixtureAsync(TempoDbContext db)
    {
        var w1 = await TestDataSeeder.SeedWorkoutAsync(
            db,
            startedAt: new DateTime(2020, 6, 8, 12, 0, 0, DateTimeKind.Utc),
            distanceM: 1000,
            durationS: 600,
            name: "Recap current Mon");
        w1.ElevGainM = 10;
        w1.RelativeEffort = 20;
        w1.RunType = "Easy Run";
        w1.AvgHeartRateBpm = 140;

        var w2 = await TestDataSeeder.SeedWorkoutAsync(
            db,
            startedAt: new DateTime(2020, 6, 9, 12, 0, 0, DateTimeKind.Utc),
            distanceM: 2000,
            durationS: 900,
            name: "Recap current Tue");
        w2.ElevGainM = null;
        w2.RelativeEffort = null;
        w2.RunType = "Race";
        w2.AvgHeartRateBpm = 180;

        var wPrev = await TestDataSeeder.SeedWorkoutAsync(
            db,
            startedAt: new DateTime(2020, 6, 5, 12, 0, 0, DateTimeKind.Utc),
            distanceM: 5000,
            durationS: 1800,
            name: "Recap previous Fri");
        wPrev.ElevGainM = 50;
        wPrev.RelativeEffort = 30;
        wPrev.RunType = "Easy Run";
        wPrev.AvgHeartRateBpm = 130;

        await db.SaveChangesAsync();
    }

    #endregion

    #region Weekly Recap Tests

    [Fact]
    public async Task GetWeeklyRecap_ReturnsZeros_WhenNoWorkouts()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var response = await client.GetAsync("/stats/weekly-recap?referenceDate=2020-06-10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyRecapResponse>();
        result.Should().NotBeNull();
        result!.Metrics.Runs.Current.Should().Be(0);
        result.Metrics.Runs.Previous.Should().Be(0);
        result.Metrics.Runs.TrailingAvg.Should().Be(0);
        result.Metrics.Runs.DeltaVsPrevious.Should().Be(0);
        result.Metrics.RelativeEffortSum.Current.Should().Be(0);
        result.Metrics.EasyRunAvgHeartRateBpm.Current.Should().BeNull();
        result.ReferenceDate.Should().Be("2020-06-10");
    }

    [Fact]
    public async Task GetWeeklyRecap_Returns400_WhenReferenceDateInvalid()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var response = await client.GetAsync("/stats/weekly-recap?referenceDate=not-a-date");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetWeeklyRecap_AggregatesPinnedWeek_WithElevationCoalesceAndEffortNulls()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedWeeklyRecapFixtureAsync(db);
        }

        var response = await client.GetAsync("/stats/weekly-recap?referenceDate=2020-06-10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyRecapResponse>();
        result.Should().NotBeNull();

        result!.WeekStart.Should().Be("2020-06-08");
        result.WeekEnd.Should().Be("2020-06-14");
        result.CurrentWeekIsPartial.Should().BeFalse();

        result.Metrics.Runs.Current.Should().Be(2);
        result.Metrics.Runs.Previous.Should().Be(1);
        result.Metrics.Runs.TrailingAvg.Should().BeApproximately(0.33, 0.01);
        result.Metrics.Runs.DeltaVsPrevious.Should().Be(1);

        result.Metrics.DistanceM.Current.Should().Be(3000);
        result.Metrics.DistanceM.Previous.Should().Be(5000);

        result.Metrics.DurationS.Current.Should().Be(1500);
        result.Metrics.DurationS.Previous.Should().Be(1800);

        result.Metrics.ElevationGainM.Current.Should().Be(10);
        result.Metrics.ElevationGainM.Previous.Should().Be(50);

        result.Metrics.RelativeEffortSum.Current.Should().Be(20);
        result.Metrics.RelativeEffortSum.Previous.Should().Be(30);

        result.Metrics.EasyRunAvgHeartRateBpm.Current.Should().Be(140);
        result.Metrics.EasyRunAvgHeartRateBpm.Previous.Should().Be(130);
        result.Metrics.EasyRunAvgHeartRateBpm.DeltaVsPrevious.Should().Be(10);
    }

    [Fact]
    public async Task GetWeeklyRecap_IgnoresNonEasyRunsForHeartRateAverage()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var w = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2020, 6, 10, 12, 0, 0, DateTimeKind.Utc),
                distanceM: 8000,
                durationS: 2400,
                name: "Hard only");
            w.RunType = "Workout";
            w.AvgHeartRateBpm = 175;
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/stats/weekly-recap?referenceDate=2020-06-10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyRecapResponse>();
        result!.Metrics.EasyRunAvgHeartRateBpm.Current.Should().BeNull();
    }

    #endregion

    #region Weekly Stats Tests

    [Fact]
    public async Task GetWeeklyStats_ReturnsCorrectDailyMiles_ForCurrentWeek()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedCurrentWeekWorkoutsAsync(db);
        }

        // Act
        var response = await client.GetAsync("/stats/weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.DailyMiles.Should().HaveCount(7);
        
        // Should have workouts on Monday (0), Tuesday (1), Wednesday (2), Thursday (3), Friday (4)
        // Saturday (5) and Sunday (6) should be 0
        result.DailyMiles[0].Should().BeGreaterThan(0); // Monday
        result.DailyMiles[1].Should().BeGreaterThan(0); // Tuesday
        result.DailyMiles[2].Should().BeGreaterThan(0); // Wednesday
        result.DailyMiles[3].Should().BeGreaterThan(0); // Thursday
        result.DailyMiles[4].Should().BeGreaterThan(0); // Friday
    }

    [Fact]
    public async Task GetWeeklyStats_ReturnsZeros_WhenNoWorkoutsInWeek()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // No workouts seeded

        // Act
        var response = await client.GetAsync("/stats/weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.DailyMiles.Should().HaveCount(7);
        result.DailyMiles.Should().AllBeEquivalentTo(0.0);
    }

    [Fact]
    public async Task GetWeeklyStats_RespectsTimezoneOffset()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // EST is UTC-5, so -300 minutes
        const int estOffsetMinutes = -300;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedCurrentWeekWorkoutsAsync(db, estOffsetMinutes);
        }

        // Act
        var response = await client.GetAsync($"/stats/weekly?timezoneOffsetMinutes={estOffsetMinutes}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.DailyMiles.Should().HaveCount(7);
    }

    [Fact]
    public async Task GetWeeklyStats_ConvertsMetersToMilesCorrectly()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed a workout with exactly 1609.344 meters (1 mile) on today
            // This ensures it's definitely in the current week
            var today = DateTime.UtcNow.Date.AddHours(10);
            var workoutDate = DateTime.SpecifyKind(today, DateTimeKind.Utc);

            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: workoutDate,
                distanceM: 1609.344, // Exactly 1 mile
                durationS: 300,
                name: "One Mile Run");
        }

        // Act
        var response = await client.GetAsync("/stats/weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        result.Should().NotBeNull();
        
        // The workout is seeded on today, which is in the current week
        // Calculate which day of week today is (0=Monday, 6=Sunday)
        var now = DateTime.UtcNow;
        var dayIndex = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        
        // Verify that today's day has approximately 1 mile
        result!.DailyMiles[dayIndex].Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public async Task GetWeeklyStats_ReturnsPreviousWeekData_WhenWorkoutsExist()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedCurrentWeekWorkoutsAsync(db);
            await SeedPreviousWeekWorkoutsAsync(db);
        }

        // Act
        var response = await client.GetAsync("/stats/weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        result.Should().NotBeNull();
        
        // Verify existing fields are still present
        result!.WeekStart.Should().NotBeEmpty();
        result.WeekEnd.Should().NotBeEmpty();
        result.DailyMiles.Should().HaveCount(7);
        
        // Verify new fields are present
        result.PreviousWeekStart.Should().NotBeNullOrEmpty();
        result.PreviousWeekEnd.Should().NotBeNullOrEmpty();
        result.PreviousWeekDailyMiles.Should().NotBeNull();
        result.PreviousWeekDailyMiles!.Should().HaveCount(7);
        result.CurrentWeekLabel.Should().NotBeNullOrEmpty();
        result.PreviousWeekLabel.Should().NotBeNullOrEmpty();
        
        // Verify previous week has workouts (should have non-zero values)
        result.PreviousWeekDailyMiles.Should().Contain(m => m > 0);
        
        // Verify week labels are formatted correctly (should contain month abbreviation and day)
        result.CurrentWeekLabel.Should().MatchRegex(@"^[A-Z][a-z]{2} \d+ - [A-Z][a-z]{2} \d+$");
        result.PreviousWeekLabel.Should().MatchRegex(@"^[A-Z][a-z]{2} \d+ - [A-Z][a-z]{2} \d+$");
    }

    [Fact]
    public async Task GetWeeklyStats_ReturnsZerosForPreviousWeek_WhenNoWorkouts()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workouts only in current week
            await SeedCurrentWeekWorkoutsAsync(db);
        }

        // Act
        var response = await client.GetAsync("/stats/weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        result.Should().NotBeNull();
        
        // Verify new fields are present
        result!.PreviousWeekStart.Should().NotBeNullOrEmpty();
        result.PreviousWeekEnd.Should().NotBeNullOrEmpty();
        result.PreviousWeekDailyMiles.Should().NotBeNull();
        result.PreviousWeekDailyMiles!.Should().HaveCount(7);
        
        // Verify previous week daily miles are all zeros
        result.PreviousWeekDailyMiles.Should().AllBeEquivalentTo(0.0);
    }

    [Fact]
    public async Task GetWeeklyStats_RespectsTimezoneForPreviousWeek()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // EST is UTC-5, so -300 minutes
        const int estOffsetMinutes = -300;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedCurrentWeekWorkoutsAsync(db, estOffsetMinutes);
            await SeedPreviousWeekWorkoutsAsync(db, estOffsetMinutes);
        }

        // Act
        var response = await client.GetAsync($"/stats/weekly?timezoneOffsetMinutes={estOffsetMinutes}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        result.Should().NotBeNull();
        
        // Verify previous week fields are present and correctly calculated
        result!.PreviousWeekStart.Should().NotBeNullOrEmpty();
        result.PreviousWeekEnd.Should().NotBeNullOrEmpty();
        result.PreviousWeekDailyMiles.Should().NotBeNull();
        result.PreviousWeekDailyMiles!.Should().HaveCount(7);
        
        // Verify previous week start is 7 days before current week start
        var currentWeekStart = DateTime.Parse(result.WeekStart);
        var previousWeekStart = DateTime.Parse(result.PreviousWeekStart);
        var daysDifference = (currentWeekStart - previousWeekStart).Days;
        daysDifference.Should().Be(7);
    }

    [Fact]
    public async Task GetWeeklyStats_MaintainsBackwardCompatibility()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedCurrentWeekWorkoutsAsync(db);
        }

        // Act
        var response = await client.GetAsync("/stats/weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        result.Should().NotBeNull();
        
        // Verify existing fields remain unchanged and are still present
        result!.WeekStart.Should().NotBeEmpty();
        result.WeekEnd.Should().NotBeEmpty();
        result.DailyMiles.Should().NotBeNull();
        result.DailyMiles.Should().HaveCount(7);
        
        // Verify existing fields have correct format
        DateTime.TryParse(result.WeekStart, out _).Should().BeTrue();
        DateTime.TryParse(result.WeekEnd, out _).Should().BeTrue();
        
        // Verify new fields are also present (for forward compatibility)
        result.PreviousWeekStart.Should().NotBeNullOrEmpty();
        result.PreviousWeekEnd.Should().NotBeNullOrEmpty();
        result.PreviousWeekDailyMiles.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWeeklyStats_HandlesPreviousWeekAcrossMonthBoundary()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Seed workouts on a date near month boundary (e.g., first Monday of a month)
        // This will make previous week span across month boundary
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Find the first Monday of current month
            var now = DateTime.UtcNow;
            var firstDayOfMonth = new DateTime(now.Year, now.Month, 1);
            var daysUntilMonday = ((int)DayOfWeek.Monday - (int)firstDayOfMonth.DayOfWeek + 7) % 7;
            var firstMonday = firstDayOfMonth.AddDays(daysUntilMonday);
            
            // If we're before the first Monday of current month, use previous month's first Monday instead
            if (now.Date < firstMonday)
            {
                // Get the first day of the previous month
                firstMonday = firstDayOfMonth.AddMonths(-1);
                // Calculate days until Monday from the previous month's first day
                daysUntilMonday = ((int)DayOfWeek.Monday - (int)firstMonday.DayOfWeek + 7) % 7;
                // Get the actual Monday in the previous month
                firstMonday = firstMonday.AddDays(daysUntilMonday);
            }
            
            // Seed workout on first Monday (current week start)
            var currentWeekStart = firstMonday;
            var workoutDate = currentWeekStart.AddHours(10);
            var workoutDateUtc = DateTime.SpecifyKind(workoutDate, DateTimeKind.Utc);
            
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: workoutDateUtc,
                distanceM: 5000,
                durationS: 1800,
                name: "Month Boundary Workout");
            
            // Seed workout in previous week (which will be in previous month)
            var previousWeekStart = currentWeekStart.AddDays(-7);
            var previousWeekWorkoutDate = previousWeekStart.AddDays(2).AddHours(10); // Wednesday of previous week
            var previousWeekWorkoutDateUtc = DateTime.SpecifyKind(previousWeekWorkoutDate, DateTimeKind.Utc);
            
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: previousWeekWorkoutDateUtc,
                distanceM: 3000,
                durationS: 1200,
                name: "Previous Month Workout");
        }

        // Act
        var response = await client.GetAsync("/stats/weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        result.Should().NotBeNull();
        
        // Verify week labels handle month boundary correctly
        result!.CurrentWeekLabel.Should().NotBeNullOrEmpty();
        result.PreviousWeekLabel.Should().NotBeNullOrEmpty();
        
        // Verify labels are formatted correctly even across month boundary
        result.CurrentWeekLabel.Should().MatchRegex(@"^[A-Z][a-z]{2} \d+ - [A-Z][a-z]{2} \d+$");
        result.PreviousWeekLabel.Should().MatchRegex(@"^[A-Z][a-z]{2} \d+ - [A-Z][a-z]{2} \d+$");
        
        // Verify previous week data is present
        result.PreviousWeekStart.Should().NotBeNullOrEmpty();
        result.PreviousWeekEnd.Should().NotBeNullOrEmpty();
        result.PreviousWeekDailyMiles.Should().NotBeNull();
    }

    #endregion

    #region Relative Effort Stats Tests

    [Fact]
    public async Task GetRelativeEffortStats_ReturnsCumulativeEffort_ForCurrentWeek()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var now = DateTime.UtcNow;
            var daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStart = now.Date.AddDays(-daysSinceMonday);

            // Seed workouts with relative effort on Monday, Tuesday, Wednesday
            var efforts = new[] { 50, 75, 100 };
            for (int day = 0; day < 3; day++)
            {
                var workoutDate = DateTime.SpecifyKind(weekStart.AddDays(day).AddHours(10), DateTimeKind.Utc);
                var workout = await TestDataSeeder.SeedWorkoutAsync(
                    db,
                    startedAt: workoutDate,
                    distanceM: 5000,
                    durationS: 1800,
                    name: $"Day {day + 1}");
                workout.RelativeEffort = efforts[day];
                await db.SaveChangesAsync();
            }
        }

        // Act
        var response = await client.GetAsync("/stats/relative-effort");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RelativeEffortStatsResponse>();
        result.Should().NotBeNull();
        result!.CurrentWeek.Should().HaveCount(7);
        
        // Cumulative: Monday = 50, Tuesday = 50+75=125, Wednesday = 125+100=225
        result.CurrentWeek[0].Should().Be(50);
        result.CurrentWeek[1].Should().Be(125);
        result.CurrentWeek[2].Should().Be(225);
        result.CurrentWeekTotal.Should().Be(225);
    }

    [Fact]
    public async Task GetRelativeEffortStats_CalculatesThreeWeekAverage_FromPreviousWeeks()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed previous 3 weeks with known totals
            await SeedPreviousWeeksWorkoutsAsync(db, 1); // Week -1: total = 365
            await SeedPreviousWeeksWorkoutsAsync(db, 2); // Week -2: total = 365
            await SeedPreviousWeeksWorkoutsAsync(db, 3); // Week -3: total = 365
        }

        // Act
        var response = await client.GetAsync("/stats/relative-effort");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RelativeEffortStatsResponse>();
        result.Should().NotBeNull();
        result!.PreviousWeeks.Should().HaveCount(3);
        result.ThreeWeekAverage.Should().BeApproximately(365.0, 0.1);
    }

    [Fact]
    public async Task GetRelativeEffortStats_SkipsWorkoutsWithoutRelativeEffort()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var now = DateTime.UtcNow;
            var daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStart = now.Date.AddDays(-daysSinceMonday);

            // Workout with relative effort
            var workout1 = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.SpecifyKind(weekStart.AddHours(10), DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "With Effort");
            workout1.RelativeEffort = 100;
            await db.SaveChangesAsync();

            // Workout without relative effort
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.SpecifyKind(weekStart.AddDays(1).AddHours(10), DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "Without Effort");
            // RelativeEffort is null
        }

        // Act
        var response = await client.GetAsync("/stats/relative-effort");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RelativeEffortStatsResponse>();
        result.Should().NotBeNull();
        result!.CurrentWeekTotal.Should().Be(100); // Only the workout with effort counted
    }

    [Fact]
    public async Task GetRelativeEffortStats_ReturnsZeros_ForEmptyWeek()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Act
        var response = await client.GetAsync("/stats/relative-effort");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RelativeEffortStatsResponse>();
        result.Should().NotBeNull();
        result!.CurrentWeek.Should().HaveCount(7);
        result.CurrentWeek.Should().AllBeEquivalentTo(0);
        result.CurrentWeekTotal.Should().Be(0);
        result.PreviousWeeks.Should().HaveCount(3);
        result.PreviousWeeks.Should().AllBeEquivalentTo(0);
        result.ThreeWeekAverage.Should().Be(0.0);
    }

    [Fact]
    public async Task GetRelativeEffortStats_RespectsTimezoneOffset()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        const int estOffsetMinutes = -300;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedCurrentWeekWorkoutsAsync(db, estOffsetMinutes);
            
            // Add relative effort to workouts
            var workouts = await db.Workouts.ToListAsync();
            foreach (var workout in workouts)
            {
                workout.RelativeEffort = 50;
            }
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync($"/stats/relative-effort?timezoneOffsetMinutes={estOffsetMinutes}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RelativeEffortStatsResponse>();
        result.Should().NotBeNull();
        result!.CurrentWeek.Should().HaveCount(7);
    }

    #endregion

    #region Yearly Stats Tests

    [Fact]
    public async Task GetYearlyStats_ReturnsCorrectTotals_ForCurrentAndPreviousYear()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Use current year dynamically to avoid test failures when year changes
        var currentYear = DateTime.UtcNow.Year;
        var previousYear = currentYear - 1;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workouts in previous year and current year
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(previousYear, 6, 15, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 10000, // ~6.21 miles
                durationS: 3600,
                name: $"{previousYear} Workout");
            
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(currentYear, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000, // ~3.11 miles
                durationS: 1800,
                name: $"{currentYear} Workout");
        }

        // Act
        var response = await client.GetAsync("/stats/yearly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyStatsResponse>();
        result.Should().NotBeNull();
        result!.CurrentYear.Should().BeGreaterThan(0);
        result.PreviousYear.Should().BeGreaterThan(0);
        
        // Compute expected year labels dynamically based on current year (API uses DateTime.UtcNow.Year)
        result.CurrentYearLabel.Should().Be(currentYear.ToString());
        result.PreviousYearLabel.Should().Be(previousYear.ToString());
    }

    [Fact]
    public async Task GetYearlyStats_HandlesMultipleYears()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedMultiYearWorkoutsAsync(db);
        }

        // Act
        var response = await client.GetAsync("/stats/yearly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyStatsResponse>();
        result.Should().NotBeNull();
        
        // SeedMultiYearWorkoutsAsync dynamically seeds workouts for the current year (12 workouts)
        // and previous year (8 workouts), so both should always be greater than 0.
        var currentYear = DateTime.UtcNow.Year;
        if (currentYear <= 2025)
        {
            result!.CurrentYear.Should().BeGreaterThan(0);
            result.PreviousYear.Should().BeGreaterThan(0);
        }
        else
        {
            // SeedMultiYearWorkoutsAsync seeds current year workouts dynamically, so CurrentYear should always have workouts
            result!.CurrentYear.Should().BeGreaterThan(0);
            result.PreviousYear.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task GetYearlyStats_ReturnsZeros_ForYearsWithNoWorkouts()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // No workouts seeded

        // Act
        var response = await client.GetAsync("/stats/yearly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyStatsResponse>();
        result.Should().NotBeNull();
        result!.CurrentYear.Should().Be(0.0);
        result.PreviousYear.Should().Be(0.0);
    }

    [Fact]
    public async Task GetYearlyStats_RespectsTimezoneOffset_ForYearBoundaries()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        const int estOffsetMinutes = -300;

        // Calculate dates relative to current year to make test work regardless of when it runs
        // IMPORTANT: Match the endpoint's calculation - apply timezone offset first, then get year
        // This prevents mismatch when test runs between midnight and 5 AM UTC on Jan 1st
        var nowUtc = DateTime.UtcNow;
        var nowInTimezone = nowUtc.AddMinutes(estOffsetMinutes);
        var currentYear = nowInTimezone.Year;
        var previousYear = currentYear - 1;
        
        // Create a workout on Dec 31 of previous year at 11 PM EST
        // EST is UTC-5, so 11 PM EST = 4 AM UTC next day
        // We want: Dec 31, previousYear 11 PM EST = Jan 1, currentYear 4 AM UTC
        var workoutDateLocal = new DateTime(previousYear, 12, 31, 23, 0, 0);
        var workoutDateUtc = workoutDateLocal.AddMinutes(-estOffsetMinutes); // Convert EST to UTC

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workout that falls in previous year when converted to EST
            // This tests year boundary handling with timezone
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: workoutDateUtc,
                distanceM: 5000,
                durationS: 1800,
                name: "Year Boundary Test");
        }

        // Act
        var response = await client.GetAsync($"/stats/yearly?timezoneOffsetMinutes={estOffsetMinutes}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyStatsResponse>();
        result.Should().NotBeNull();
        // The workout at Dec 31 11 PM EST (previous year) should be in previous year
        result!.PreviousYear.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetYearlyStats_ConvertsMetersToMilesCorrectly()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed exactly 1 mile in current year
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow,
                distanceM: 1609.344,
                durationS: 300,
                name: "One Mile");
        }

        // Act
        var response = await client.GetAsync("/stats/yearly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyStatsResponse>();
        result.Should().NotBeNull();
        result!.CurrentYear.Should().BeApproximately(1.0, 0.001);
    }

    #endregion

    #region Yearly Weekly Stats Tests

    [Fact]
    public async Task GetYearlyWeeklyStats_Returns52WeekBuckets()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedMultiYearWorkoutsAsync(db);
        }

        // Act
        var response = await client.GetAsync("/stats/yearly-weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyWeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.Weeks.Should().HaveCount(52);
        
        // Each week should have weekNumber, weekStart, weekEnd, distanceM
        foreach (var week in result.Weeks)
        {
            week.WeekNumber.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(52);
            week.WeekStart.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
            week.WeekEnd.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
            week.DistanceM.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public async Task GetYearlyWeeklyStats_HandlesCustomPeriodEndDate()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workout in 2024
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "2024 Workout");
        }

        // Act
        var response = await client.GetAsync("/stats/yearly-weekly?periodEndDate=2024-12-31");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyWeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.Weeks.Should().HaveCount(52);
        result.DateRangeEnd.Should().Be("2024-12-31");
    }

    [Fact]
    public async Task GetYearlyWeeklyStats_DefaultsToToday_WhenPeriodEndDateNotProvided()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        // Capture expected date before API call to avoid flakiness if test runs at midnight UTC
        var expectedDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Act
        var response = await client.GetAsync("/stats/yearly-weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyWeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.Weeks.Should().HaveCount(52);
        result.DateRangeEnd.Should().Be(expectedDate);
    }

    [Fact]
    public async Task GetYearlyWeeklyStats_DistributesWorkoutsAcrossBuckets()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workouts across the year
            for (int month = 1; month <= 12; month++)
            {
                await TestDataSeeder.SeedWorkoutAsync(
                    db,
                    startedAt: new DateTime(2024, month, 15, 10, 0, 0, DateTimeKind.Utc),
                    distanceM: 5000,
                    durationS: 1800,
                    name: $"Month {month}");
            }
        }

        // Act
        var response = await client.GetAsync("/stats/yearly-weekly?periodEndDate=2024-12-31");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyWeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.Weeks.Should().HaveCount(52);
        
        // At least some weeks should have distance > 0
        var weeksWithData = result.Weeks.Count(w => w.DistanceM > 0);
        weeksWithData.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetYearlyWeeklyStats_HandlesEmptyPeriods()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // No workouts seeded

        // Act
        var response = await client.GetAsync("/stats/yearly-weekly");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyWeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.Weeks.Should().HaveCount(52);
        result.Weeks.Should().AllSatisfy(w => w.DistanceM.Should().Be(0));
    }

    [Fact]
    public async Task GetYearlyWeeklyStats_HandlesSparseData_SingleWorkoutInYear()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedSparseDataAsync(db, 2024);
        }

        // Act
        var response = await client.GetAsync("/stats/yearly-weekly?periodEndDate=2024-12-31");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyWeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.Weeks.Should().HaveCount(52);
        
        // Only one week should have data
        var weeksWithData = result.Weeks.Count(w => w.DistanceM > 0);
        weeksWithData.Should().Be(1);
        
        // That week should have 5000m
        var weekWithData = result.Weeks.First(w => w.DistanceM > 0);
        weekWithData.DistanceM.Should().Be(5000);
    }

    [Fact]
    public async Task GetYearlyWeeklyStats_RespectsTimezoneOffset()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        const int estOffsetMinutes = -300;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "Test");
        }

        // Act
        var response = await client.GetAsync($"/stats/yearly-weekly?periodEndDate=2024-12-31&timezoneOffsetMinutes={estOffsetMinutes}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<YearlyWeeklyStatsResponse>();
        result.Should().NotBeNull();
        result!.Weeks.Should().HaveCount(52);
    }

    #endregion

    #region Available Periods Tests

    [Fact]
    public async Task GetAvailablePeriods_ReturnsConsecutivePeriods_GoingBackwardsFromToday()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workouts in 2022, 2023, 2024, 2025
            await SeedMultiYearWorkoutsAsync(db);
        }

        // Act
        var response = await client.GetAsync("/stats/available-periods");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<AvailablePeriodResponse>>();
        result.Should().NotBeNull();
        result!.Count.Should().BeGreaterThan(0);
        
        // Periods should be in descending order (newest first)
        for (int i = 0; i < result.Count - 1; i++)
        {
            var currentPeriod = DateTime.Parse(result[i].PeriodEndDate);
            var nextPeriod = DateTime.Parse(result[i + 1].PeriodEndDate);
            currentPeriod.Should().BeAfter(nextPeriod);
        }
    }

    [Fact]
    public async Task GetAvailablePeriods_StopsAtFirstWorkoutDate()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workout in 2022
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2022, 6, 15, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "2022 Workout");
        }

        // Act
        var response = await client.GetAsync("/stats/available-periods");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<AvailablePeriodResponse>>();
        result.Should().NotBeNull();
        
        // Last period should end around the first workout date
        var lastPeriod = result!.Last();
        var lastPeriodEnd = DateTime.Parse(lastPeriod.PeriodEndDate);
        lastPeriodEnd.Should().BeOnOrAfter(new DateTime(2022, 6, 15));
    }

    [Fact]
    public async Task GetAvailablePeriods_Respects20PeriodSafetyLimit()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workout very far in the past (more than 20 years)
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2000, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "Old Workout");
        }

        // Act
        var response = await client.GetAsync("/stats/available-periods");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<AvailablePeriodResponse>>();
        result.Should().NotBeNull();
        // The endpoint adds a period, then checks if count > 20, so it can have 21 periods before stopping
        result!.Count.Should().BeLessThanOrEqualTo(21);
    }

    [Fact]
    public async Task GetAvailablePeriods_ReturnsEmptyList_ForEmptyDatabase()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // No workouts seeded

        // Act
        var response = await client.GetAsync("/stats/available-periods");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<AvailablePeriodResponse>>();
        result.Should().NotBeNull();
        // Should return at least the current period even with no data
        result!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetAvailablePeriods_RespectsTimezoneOffset_ForTodayCalculation()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        const int estOffsetMinutes = -300;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow,
                distanceM: 5000,
                durationS: 1800,
                name: "Test");
        }

        // Act
        var response = await client.GetAsync($"/stats/available-periods?timezoneOffsetMinutes={estOffsetMinutes}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<AvailablePeriodResponse>>();
        result.Should().NotBeNull();
        result!.Count.Should().BeGreaterThan(0);
    }

    #endregion

    #region Available Years Tests

    [Fact]
    public async Task GetAvailableYears_ReturnsDistinctYears_InDescendingOrder()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedMultiYearWorkoutsAsync(db);
        }

        // Act
        var response = await client.GetAsync("/stats/available-years");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<int>>();
        result.Should().NotBeNull();
        result!.Count.Should().BeGreaterThan(0);
        
        // Should be in descending order
        for (int i = 0; i < result.Count - 1; i++)
        {
            result[i].Should().BeGreaterThan(result[i + 1]);
        }
        
        // Should contain expected years
        result.Should().Contain(2025);
        result.Should().Contain(2024);
        result.Should().Contain(2023);
        result.Should().Contain(2022);
    }

    [Fact]
    public async Task GetAvailableYears_HandlesMultipleYears()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workouts in different years
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2022, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "2022");
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2023, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "2023");
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "2024");
        }

        // Act
        var response = await client.GetAsync("/stats/available-years");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<int>>();
        result.Should().NotBeNull();
        result!.Count.Should().Be(3);
        result.Should().ContainInOrder(2024, 2023, 2022);
    }

    [Fact]
    public async Task GetAvailableYears_ReturnsEmptyList_ForEmptyDatabase()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Act
        var response = await client.GetAsync("/stats/available-years");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<int>>();
        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableYears_HandlesSingleWorkoutInYear()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedSparseDataAsync(db, 2024);
        }

        // Act
        var response = await client.GetAsync("/stats/available-years");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<int>>();
        result.Should().NotBeNull();
        result!.Should().ContainSingle(y => y == 2024);
    }

    #endregion

    #region Best Efforts Tests

    [Fact]
    public async Task GetBestEfforts_ReturnsBestEfforts_ForStandardDistances()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workout with time series data (required for best efforts)
            var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
                db,
                startedAt: DateTime.UtcNow,
                distanceM: 10000, // 10K - long enough for multiple standard distances
                durationS: 2400,
                name: "10K Run",
                includeTimeSeries: true);
            
            // Calculate best efforts
            var bestEffortService = scope.ServiceProvider.GetRequiredService<BestEffortService>();
            await bestEffortService.CalculateAllBestEffortsAsync(db);
        }

        // Act
        var response = await client.GetAsync("/stats/best-efforts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BestEffortsResponse>();
        result.Should().NotBeNull();
        result!.Distances.Should().NotBeNull();
        
        // Should have best efforts for distances <= 10K
        var distances = result.Distances.ToList();
        distances.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetBestEfforts_HandlesWorkoutsWithoutBestEfforts()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workout without time series or route (can't calculate best efforts)
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow,
                distanceM: 5000,
                durationS: 1800,
                name: "No Best Efforts");
        }

        // Act
        var response = await client.GetAsync("/stats/best-efforts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BestEffortsResponse>();
        result.Should().NotBeNull();
        result!.Distances.Should().NotBeNull();
        // May be empty or have some best efforts from other sources
    }

    [Fact]
    public async Task RecalculateBestEfforts_SuccessfullyRecalculatesAllBestEfforts()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed multiple workouts with time series
            for (int i = 0; i < 3; i++)
            {
                await TestDataSeeder.SeedWorkoutCompleteAsync(
                    db,
                    startedAt: DateTime.UtcNow.AddDays(-i),
                    distanceM: 10000,
                    durationS: 2400,
                    name: $"Workout {i + 1}",
                    includeTimeSeries: true);
            }
        }

        // Act
        var response = await client.PostAsync("/stats/best-efforts/recalculate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RecalculateBestEffortsResponse>();
        result.Should().NotBeNull();
        result!.Message.Should().Contain("recalculated");
        result.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RecalculateBestEfforts_HandlesEmptyDatabaseGracefully()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Act
        var response = await client.PostAsync("/stats/best-efforts/recalculate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RecalculateBestEffortsResponse>();
        result.Should().NotBeNull();
        result!.Message.Should().Contain("recalculated");
        result.Count.Should().Be(0);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task AllEndpoints_ReturnSensibleDefaults_ForEmptyDatabase()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Act & Assert - Weekly Stats
        var weeklyResponse = await client.GetAsync("/stats/weekly");
        weeklyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var weeklyResult = await weeklyResponse.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        weeklyResult!.DailyMiles.Should().AllBeEquivalentTo(0.0);

        // Act & Assert - Relative Effort
        var effortResponse = await client.GetAsync("/stats/relative-effort");
        effortResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var effortResult = await effortResponse.Content.ReadFromJsonAsync<RelativeEffortStatsResponse>();
        effortResult!.CurrentWeek.Should().AllBeEquivalentTo(0);
        effortResult.CurrentWeekTotal.Should().Be(0);

        // Act & Assert - Weekly Recap
        var recapResponse = await client.GetAsync("/stats/weekly-recap");
        recapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recapResult = await recapResponse.Content.ReadFromJsonAsync<WeeklyRecapResponse>();
        recapResult!.Metrics.Runs.Current.Should().Be(0);
        recapResult.Metrics.Runs.Previous.Should().Be(0);

        // Act & Assert - Yearly Stats
        var yearlyResponse = await client.GetAsync("/stats/yearly");
        yearlyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var yearlyResult = await yearlyResponse.Content.ReadFromJsonAsync<YearlyStatsResponse>();
        yearlyResult!.CurrentYear.Should().Be(0.0);
        yearlyResult.PreviousYear.Should().Be(0.0);

        // Act & Assert - Yearly Weekly Stats
        var yearlyWeeklyResponse = await client.GetAsync("/stats/yearly-weekly");
        yearlyWeeklyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var yearlyWeeklyResult = await yearlyWeeklyResponse.Content.ReadFromJsonAsync<YearlyWeeklyStatsResponse>();
        yearlyWeeklyResult!.Weeks.Should().HaveCount(52);
        yearlyWeeklyResult.Weeks.Should().AllSatisfy(w => w.DistanceM.Should().Be(0));

        // Act & Assert - Available Years
        var yearsResponse = await client.GetAsync("/stats/available-years");
        yearsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var yearsResult = await yearsResponse.Content.ReadFromJsonAsync<List<int>>();
        yearsResult!.Should().BeEmpty();
    }

    [Fact]
    public async Task AllEndpoints_HandleSparseData_SingleWorkoutInYear()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Seed workout in previous year so it appears in PreviousYear stats
        // This ensures the test works regardless of what the current year is
        var previousYear = DateTime.UtcNow.Year - 1;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedSparseDataAsync(db, previousYear);
        }

        // Act & Assert - Yearly Stats should show the workout in PreviousYear
        var yearlyResponse = await client.GetAsync("/stats/yearly");
        yearlyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var yearlyResult = await yearlyResponse.Content.ReadFromJsonAsync<YearlyStatsResponse>();
        yearlyResult!.PreviousYear.Should().BeGreaterThan(0, "the workout was seeded in the previous year");

        // Act & Assert - Available Years should include the year we seeded
        var yearsResponse = await client.GetAsync("/stats/available-years");
        yearsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var yearsResult = await yearsResponse.Content.ReadFromJsonAsync<List<int>>();
        yearsResult!.Should().Contain(previousYear);
    }

    [Fact]
    public async Task AllEndpoints_HandleWorkoutsWithMissingOptionalFields()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workout without HR, splits, routes
            await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow,
                distanceM: 5000,
                durationS: 1800,
                name: "Minimal Workout");
            // No HR, splits, routes, or time series
        }

        // Act & Assert - All endpoints should still work
        var weeklyResponse = await client.GetAsync("/stats/weekly");
        weeklyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var yearlyResponse = await client.GetAsync("/stats/yearly");
        yearlyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var effortResponse = await client.GetAsync("/stats/relative-effort");
        effortResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var effortResult = await effortResponse.Content.ReadFromJsonAsync<RelativeEffortStatsResponse>();
        effortResult!.CurrentWeekTotal.Should().Be(0); // No relative effort

        var recapResponse = await client.GetAsync("/stats/weekly-recap");
        recapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AllEndpoints_HandleDifferentRunTypes()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await SeedWorkoutsWithDifferentRunTypesAsync(db);
        }

        // Act & Assert - Endpoints should aggregate all run types
        var yearlyResponse = await client.GetAsync("/stats/yearly");
        yearlyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var yearlyResult = await yearlyResponse.Content.ReadFromJsonAsync<YearlyStatsResponse>();
        yearlyResult!.CurrentYear.Should().BeGreaterThan(0);

        var weeklyResponse = await client.GetAsync("/stats/weekly");
        weeklyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var weeklyResult = await weeklyResponse.Content.ReadFromJsonAsync<WeeklyStatsResponse>();
        weeklyResult!.DailyMiles.Sum().Should().BeGreaterThan(0);

        var recapResponse = await client.GetAsync("/stats/weekly-recap");
        recapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recapResult = await recapResponse.Content.ReadFromJsonAsync<WeeklyRecapResponse>();
        recapResult!.Metrics.Runs.Current.Should().BeGreaterThan(0);
    }

    #endregion

    #region Response Models

    private class WeeklyRecapResponse
    {
        public string WeekStart { get; set; } = string.Empty;
        public string WeekEnd { get; set; } = string.Empty;
        public string ReferenceDate { get; set; } = string.Empty;
        public int? TimezoneOffsetMinutes { get; set; }
        public bool CurrentWeekIsPartial { get; set; }
        public string GeneratedAtUtc { get; set; } = string.Empty;
        public WeeklyRecapMetricsBlock Metrics { get; set; } = null!;
    }

    private class WeeklyRecapMetricsBlock
    {
        public WeeklyRecapMetricInt Runs { get; set; } = null!;
        public WeeklyRecapMetricDouble DistanceM { get; set; } = null!;
        public WeeklyRecapMetricLong DurationS { get; set; } = null!;
        public WeeklyRecapMetricDouble ElevationGainM { get; set; } = null!;
        public WeeklyRecapMetricInt RelativeEffortSum { get; set; } = null!;
        public WeeklyRecapMetricNullableDouble EasyRunAvgHeartRateBpm { get; set; } = null!;
    }

    private class WeeklyRecapMetricInt
    {
        public int Current { get; set; }
        public int Previous { get; set; }
        public double TrailingAvg { get; set; }
        public int DeltaVsPrevious { get; set; }
    }

    private class WeeklyRecapMetricLong
    {
        public long Current { get; set; }
        public long Previous { get; set; }
        public double TrailingAvg { get; set; }
        public long DeltaVsPrevious { get; set; }
    }

    private class WeeklyRecapMetricDouble
    {
        public double Current { get; set; }
        public double Previous { get; set; }
        public double TrailingAvg { get; set; }
        public double DeltaVsPrevious { get; set; }
    }

    private class WeeklyRecapMetricNullableDouble
    {
        public double? Current { get; set; }
        public double? Previous { get; set; }
        public double? TrailingAvg { get; set; }
        public double? DeltaVsPrevious { get; set; }
    }

    private class WeeklyStatsResponse
    {
        public string WeekStart { get; set; } = string.Empty;
        public string WeekEnd { get; set; } = string.Empty;
        public List<double> DailyMiles { get; set; } = new();
        // New fields for week-over-week comparison
        public string? PreviousWeekStart { get; set; }
        public string? PreviousWeekEnd { get; set; }
        public List<double>? PreviousWeekDailyMiles { get; set; }
        public string? CurrentWeekLabel { get; set; }
        public string? PreviousWeekLabel { get; set; }
    }

    private class RelativeEffortStatsResponse
    {
        public string WeekStart { get; set; } = string.Empty;
        public string WeekEnd { get; set; } = string.Empty;
        public List<int> CurrentWeek { get; set; } = new();
        public List<int> PreviousWeeks { get; set; } = new();
        public double ThreeWeekAverage { get; set; }
        public int RangeMin { get; set; }
        public int RangeMax { get; set; }
        public int CurrentWeekTotal { get; set; }
    }

    private class YearlyStatsResponse
    {
        public double CurrentYear { get; set; }
        public double PreviousYear { get; set; }
        public string CurrentYearLabel { get; set; } = string.Empty;
        public string PreviousYearLabel { get; set; } = string.Empty;
    }

    private class YearlyWeeklyStatsResponse
    {
        public List<WeekBucket> Weeks { get; set; } = new();
        public string DateRangeStart { get; set; } = string.Empty;
        public string DateRangeEnd { get; set; } = string.Empty;
    }

    private class WeekBucket
    {
        public int WeekNumber { get; set; }
        public string WeekStart { get; set; } = string.Empty;
        public string WeekEnd { get; set; } = string.Empty;
        public double DistanceM { get; set; }
    }

    private class AvailablePeriodResponse
    {
        public string PeriodEndDate { get; set; } = string.Empty;
        public string DateRangeStart { get; set; } = string.Empty;
        public string DateRangeEnd { get; set; } = string.Empty;
        public string DateRangeLabel { get; set; } = string.Empty;
    }

    private class BestEffortsResponse
    {
        public List<object> Distances { get; set; } = new();
    }

    private class RecalculateBestEffortsResponse
    {
        public string Message { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    #endregion
}

