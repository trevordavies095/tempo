using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
        var authenticatedClient = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tempo.Api.Data.TempoDbContext>();
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
