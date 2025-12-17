using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Example integration tests demonstrating the test infrastructure usage
/// </summary>
public class WorkoutIntegrationTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WorkoutIntegrationTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
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
            db.WorkoutTimeSeries.RemoveRange(db.WorkoutTimeSeries);
            db.WorkoutSplits.RemoveRange(db.WorkoutSplits);
            db.WorkoutMedia.RemoveRange(db.WorkoutMedia);
            db.WorkoutRoutes.RemoveRange(db.WorkoutRoutes);
            db.BestEfforts.RemoveRange(db.BestEfforts);
            db.Workouts.RemoveRange(db.Workouts);
            db.UserSettings.RemoveRange(db.UserSettings);
            db.Shoes.RemoveRange(db.Shoes);
            // Note: We don't clear Users - we need the test user for authentication
            
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetWorkouts_ReturnsEmptyList_WhenNoWorkouts()
    {
        // Arrange - no workouts seeded

        // Act
        var response = await _client.GetAsync("/workouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized); // Requires authentication
    }

    [Fact]
    public async Task GetWorkouts_ReturnsEmptyList_WhenAuthenticatedAndNoWorkouts()
    {
        // Arrange
        await EnsureCleanDatabaseAsync(); // Ensure clean database before test (preserves test user)
        // CreateAuthenticatedClientAsync will create the user if it doesn't exist
        var authenticatedClient = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Act
        var response = await authenticatedClient.GetAsync("/workouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetWorkouts_ReturnsWorkouts_WhenWorkoutsExist()
    {
        // Arrange
        await EnsureCleanDatabaseAsync(); // Ensure clean database before test (preserves test user)
        // CreateAuthenticatedClientAsync will create the user if it doesn't exist
        var authenticatedClient = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Seed test data for this specific test
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Seed workouts for this test
            await TestDataSeeder.SeedWorkoutAsync(db, name: "Morning Run");
            await TestDataSeeder.SeedWorkoutAsync(db, name: "Evening Run");
        }

        // Act
        var response = await authenticatedClient.GetAsync("/workouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    private class WorkoutsListResponse
    {
        public List<object> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    [Fact]
    public async Task HealthCheck_ReturnsOk_WithoutAuthentication()
    {
        // Arrange
        var unauthenticatedClient = TestHttpClientFactory.CreateUnauthenticatedClient(_factory);

        // Act
        var response = await unauthenticatedClient.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("healthy");
    }
}
