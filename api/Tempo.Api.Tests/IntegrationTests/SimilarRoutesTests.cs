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

/// <summary>
/// Integration tests for GetSimilarRoutes endpoint
/// </summary>
[Collection("Integration Tests")]
public class SimilarRoutesTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public SimilarRoutesTests(TempoWebApplicationFactory factory)
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

    /// <summary>
    /// Helper method to create a route GeoJSON with specified coordinates
    /// </summary>
    private string CreateRouteGeoJson(List<double[]> coordinates)
    {
        return JsonSerializer.Serialize(new
        {
            type = "LineString",
            coordinates = coordinates
        });
    }

    [Fact]
    public async Task GetSimilarRoutes_ReturnsSimilarRoutes_WhenSimilarRoutesExist()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout currentWorkout;
        Workout similarWorkout1;
        Workout similarWorkout2;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();

            // Create current workout with route
            currentWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-1),
                distanceM: 5000,
                durationS: 1800,
                name: "Current Run");

            // Create route for current workout (start at 0,0, end at 0.01, 0.01)
            var currentRouteCoords = new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 0.002, 0.002 },
                new[] { 0.005, 0.005 },
                new[] { 0.008, 0.008 },
                new[] { 0.01, 0.01 }
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, currentWorkout, currentRouteCoords);

            // Create similar workout 1 (same route, slightly different time)
            similarWorkout1 = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-10),
                distanceM: 5100, // Within 10% of 5000
                durationS: 1900, // Slower
                name: "Previous Run 1");
            similarWorkout1.RelativeEffort = 85;
            similarWorkout1.ElevGainM = 150.0;
            await db.SaveChangesAsync();

            // Create route for similar workout 1 (same start/end, slightly different path)
            var similarRoute1Coords = new List<double[]>
            {
                new[] { 0.0, 0.0 }, // Same start
                new[] { 0.0025, 0.0025 }, // Slightly different
                new[] { 0.005, 0.005 },
                new[] { 0.0075, 0.0075 }, // Slightly different
                new[] { 0.01, 0.01 } // Same end
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, similarWorkout1, similarRoute1Coords);

            // Create similar workout 2 (same route, faster time)
            similarWorkout2 = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-20),
                distanceM: 4950, // Within 10% of 5000
                durationS: 1700, // Faster
                name: "Previous Run 2");
            similarWorkout2.RelativeEffort = 90;
            similarWorkout2.ElevGainM = 140.0;
            await db.SaveChangesAsync();

            // Create route for similar workout 2 (same start/end, slightly different path)
            var similarRoute2Coords = new List<double[]>
            {
                new[] { 0.0, 0.0 }, // Same start
                new[] { 0.0015, 0.0015 }, // Slightly different
                new[] { 0.005, 0.005 },
                new[] { 0.0085, 0.0085 }, // Slightly different
                new[] { 0.01, 0.01 } // Same end
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, similarWorkout2, similarRoute2Coords);
        }

        // Act
        var response = await client.GetAsync($"/workouts/{currentWorkout.Id}/similar-routes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<SimilarRouteResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCountGreaterThan(0);

        // Verify response structure
        var firstMatch = result[0];
        firstMatch.Should().NotBeNull();
        firstMatch.WorkoutId.Should().NotBeEmpty();
        firstMatch.StartedAt.Should().BeBefore(DateTime.UtcNow);
        firstMatch.DurationS.Should().BeGreaterThan(0);
        firstMatch.DistanceM.Should().BeGreaterThan(0);
        firstMatch.AvgPaceS.Should().BeGreaterThan(0);
        firstMatch.SimilarityScore.Should().BeGreaterThan(0);
        firstMatch.TimeDifferenceS.Should().NotBeNull();
        firstMatch.PaceDifferenceS.Should().NotBeNull();

        // Verify time/pace differences are calculated correctly
        // For similarWorkout1: 1900 - 1800 = 100 (slower, positive)
        // For similarWorkout2: 1700 - 1800 = -100 (faster, negative)
        var match1 = result.FirstOrDefault(r => r.WorkoutId == similarWorkout1.Id);
        if (match1 != null)
        {
            match1.TimeDifferenceS.Should().Be(100); // Slower
            match1.PaceDifferenceS.Should().BeGreaterThan(0); // Slower pace
            match1.RelativeEffort.Should().Be(85);
            match1.ElevGainM.Should().Be(150.0);
        }

        var match2 = result.FirstOrDefault(r => r.WorkoutId == similarWorkout2.Id);
        if (match2 != null)
        {
            match2.TimeDifferenceS.Should().Be(-100); // Faster
            match2.PaceDifferenceS.Should().BeLessThan(0); // Faster pace
            match2.RelativeEffort.Should().Be(90);
            match2.ElevGainM.Should().Be(140.0);
        }
    }

    [Fact]
    public async Task GetSimilarRoutes_ReturnsEmptyArray_WhenNoSimilarRoutesFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout currentWorkout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();

            // Create current workout with route
            currentWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-1),
                distanceM: 5000,
                durationS: 1800,
                name: "Current Run");

            // Create route for current workout
            var currentRouteCoords = new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 0.01, 0.01 }
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, currentWorkout, currentRouteCoords);

            // Create a workout with a completely different route (far away)
            var differentWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-10),
                distanceM: 5000,
                durationS: 1800,
                name: "Different Route");
            
            var differentRouteCoords = new List<double[]>
            {
                new[] { 10.0, 10.0 }, // Far away from current route
                new[] { 10.01, 10.01 }
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, differentWorkout, differentRouteCoords);
        }

        // Act
        var response = await client.GetAsync($"/workouts/{currentWorkout.Id}/similar-routes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<SimilarRouteResponse>>();
        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSimilarRoutes_Returns404_WhenWorkoutNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/workouts/{nonExistentId}/similar-routes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task GetSimilarRoutes_Returns400_WhenWorkoutHasNoRouteData()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();

            // Create workout without route
            workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-1),
                distanceM: 5000,
                durationS: 1800,
                name: "No Route Workout");
        }

        // Act
        var response = await client.GetAsync($"/workouts/{workout.Id}/similar-routes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("no route data");
    }

    [Fact]
    public async Task GetSimilarRoutes_RequiresAuthentication()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var unauthenticatedClient = _factory.CreateClient(); // No authentication

        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();

            workout = await TestDataSeeder.SeedWorkoutCompleteAsync(db);
        }

        // Act
        var response = await unauthenticatedClient.GetAsync($"/workouts/{workout.Id}/similar-routes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSimilarRoutes_RespectsMaxResultsParameter()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout currentWorkout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();

            // Create current workout with route
            currentWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-1),
                distanceM: 5000,
                durationS: 1800,
                name: "Current Run");

            var currentRouteCoords = new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 0.01, 0.01 }
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, currentWorkout, currentRouteCoords);

            // Create multiple similar workouts (more than maxResults)
            for (int i = 0; i < 15; i++)
            {
                var similarWorkout = await TestDataSeeder.SeedWorkoutAsync(
                    db,
                    startedAt: DateTime.UtcNow.AddDays(-(i + 2)),
                    distanceM: 5000 + (i * 10), // Slightly different distances
                    durationS: 1800 + (i * 10),
                    name: $"Similar Run {i}");

                // Create similar route (same start/end points)
                var similarRouteCoords = new List<double[]>
                {
                    new[] { 0.0, 0.0 },
                    new[] { 0.01, 0.01 }
                };
                await TestDataSeeder.SeedWorkoutWithRouteAsync(db, similarWorkout, similarRouteCoords);
            }
        }

        // Act - Request with maxResults = 5
        var response = await client.GetAsync($"/workouts/{currentWorkout.Id}/similar-routes?maxResults=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<SimilarRouteResponse>>();
        result.Should().NotBeNull();
        result!.Count.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task GetSimilarRoutes_CalculatesTimeAndPaceDifferencesCorrectly()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout currentWorkout;
        Workout fasterWorkout;
        Workout slowerWorkout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();

            // Current workout: 5000m in 1800s (5:00/km pace)
            currentWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-1),
                distanceM: 5000,
                durationS: 1800, // 30 minutes
                name: "Current Run");
            currentWorkout.AvgPaceS = 360; // 6:00/km

            var currentRouteCoords = new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 0.01, 0.01 }
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, currentWorkout, currentRouteCoords);

            // Faster workout: 5000m in 1700s (5:40/km pace)
            fasterWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-10),
                distanceM: 5000,
                durationS: 1700, // 28:20
                name: "Faster Run");
            fasterWorkout.AvgPaceS = 340; // 5:40/km
            await db.SaveChangesAsync();

            var fasterRouteCoords = new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 0.01, 0.01 }
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, fasterWorkout, fasterRouteCoords);

            // Slower workout: 5000m in 1900s (6:20/km pace)
            slowerWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-20),
                distanceM: 5000,
                durationS: 1900, // 31:40
                name: "Slower Run");
            slowerWorkout.AvgPaceS = 380; // 6:20/km
            await db.SaveChangesAsync();

            var slowerRouteCoords = new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 0.01, 0.01 }
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, slowerWorkout, slowerRouteCoords);
        }

        // Act
        var response = await client.GetAsync($"/workouts/{currentWorkout.Id}/similar-routes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<SimilarRouteResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCountGreaterThan(0);

        // Verify faster workout differences (negative = faster)
        var fasterMatch = result.FirstOrDefault(r => r.WorkoutId == fasterWorkout.Id);
        if (fasterMatch != null)
        {
            fasterMatch.TimeDifferenceS.Should().Be(-100); // 1700 - 1800 = -100 (faster)
            fasterMatch.PaceDifferenceS.Should().Be(-20); // 340 - 360 = -20 (faster pace)
        }

        // Verify slower workout differences (positive = slower)
        var slowerMatch = result.FirstOrDefault(r => r.WorkoutId == slowerWorkout.Id);
        if (slowerMatch != null)
        {
            slowerMatch.TimeDifferenceS.Should().Be(100); // 1900 - 1800 = 100 (slower)
            slowerMatch.PaceDifferenceS.Should().Be(20); // 380 - 360 = 20 (slower pace)
        }
    }

    [Fact]
    public async Task GetSimilarRoutes_IncludesAllRequiredFields()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout currentWorkout;
        Workout similarWorkout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();

            // Create current workout with route
            currentWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-1),
                distanceM: 5000,
                durationS: 1800,
                name: "Current Run");

            var currentRouteCoords = new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 0.01, 0.01 }
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, currentWorkout, currentRouteCoords);

            // Create similar workout with all fields
            similarWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: DateTime.UtcNow.AddDays(-10),
                distanceM: 5000,
                durationS: 1900,
                name: "Similar Run");
            similarWorkout.RelativeEffort = 85;
            similarWorkout.ElevGainM = 150.0;
            await db.SaveChangesAsync();

            var similarRouteCoords = new List<double[]>
            {
                new[] { 0.0, 0.0 },
                new[] { 0.01, 0.01 }
            };
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, similarWorkout, similarRouteCoords);
        }

        // Act
        var response = await client.GetAsync($"/workouts/{currentWorkout.Id}/similar-routes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<SimilarRouteResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCountGreaterThan(0);

        var match = result.FirstOrDefault(r => r.WorkoutId == similarWorkout.Id);
        match.Should().NotBeNull();
        match!.WorkoutId.Should().Be(similarWorkout.Id);
        match.StartedAt.Should().Be(similarWorkout.StartedAt);
        match.DurationS.Should().Be(similarWorkout.DurationS);
        match.DistanceM.Should().Be(similarWorkout.DistanceM);
        match.AvgPaceS.Should().Be(similarWorkout.AvgPaceS);
        match.SimilarityScore.Should().NotBeNull().And.BeGreaterThan(0);
        match.TimeDifferenceS.Should().NotBeNull();
        match.PaceDifferenceS.Should().NotBeNull();
        match.RelativeEffort.Should().Be(85);
        match.ElevGainM.Should().Be(150.0);
    }

    #region Response Models

    private class SimilarRouteResponse
    {
        public Guid WorkoutId { get; set; }
        public DateTime StartedAt { get; set; }
        public int DurationS { get; set; }
        public double DistanceM { get; set; }
        public int AvgPaceS { get; set; }
        public double? SimilarityScore { get; set; }
        public int? TimeDifferenceS { get; set; }
        public int? PaceDifferenceS { get; set; }
        public int? RelativeEffort { get; set; }
        public double? ElevGainM { get; set; }
    }

    private class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
    }

    #endregion
}

