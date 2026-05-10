using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class WorkoutTimeSeriesEndpointTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public WorkoutTimeSeriesEndpointTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task EnsureCleanDatabaseAsync()
    {
        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
    }

    [Fact]
    public async Task GetWorkoutTimeSeries_Returns404_WhenWorkoutNotFound()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var id = Guid.NewGuid();

        var response = await client.GetAsync($"/workouts/{id}/time-series");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Error.Should().Be("Workout not found");
    }

    [Fact]
    public async Task GetWorkoutTimeSeries_ReturnsEmptyItems_WhenNoHeartRateSamples()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, durationS: 600);
            await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(
                db,
                workout,
                intervalSeconds: 60,
                totalDurationS: 600,
                includeHeartRate: false,
                includeCadence: true);
        }

        var response = await client.GetAsync($"/workouts/{workout.Id}/time-series");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WorkoutTimeSeriesResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
        body.TotalPages.Should().Be(0);
        body.Page.Should().Be(1);
    }

    [Fact]
    public async Task GetWorkoutTimeSeries_Returns404_WhenNoHeartRateSamplesAndPageOutOfRange()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, durationS: 600);
            await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(
                db,
                workout,
                intervalSeconds: 60,
                totalDurationS: 600,
                includeHeartRate: false,
                includeCadence: true);
        }

        var response = await client.GetAsync($"/workouts/{workout.Id}/time-series?page=999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Error.Should().Be("Page not found");
    }

    [Fact]
    public async Task GetWorkoutTimeSeries_ReturnsOrderedHeartRateSamples()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, durationS: 1200);
            await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(
                db,
                workout,
                intervalSeconds: 10,
                totalDurationS: 1200,
                includeHeartRate: true,
                includeCadence: false);
        }

        var response = await client.GetAsync($"/workouts/{workout.Id}/time-series?pageSize=5000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WorkoutTimeSeriesResponse>();
        body.Should().NotBeNull();
        body!.TotalCount.Should().BeGreaterThan(10);
        body.Items.Should().NotBeEmpty();

        var elapsed = body.Items.Select(i => i.ElapsedSeconds).ToList();
        elapsed.Should().BeInAscendingOrder();

        body.Items.Should().OnlyContain(i => i.HeartRateBpm >= 60 && i.HeartRateBpm <= 200);
    }

    [Fact]
    public async Task GetWorkoutTimeSeries_PaginatesResults()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, durationS: 1800);
            await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(
                db,
                workout,
                intervalSeconds: 10,
                totalDurationS: 1800,
                includeHeartRate: true,
                includeCadence: false);
        }

        const int expectedSamples = 181;

        var page1 = await client.GetAsync($"/workouts/{workout.Id}/time-series?page=1&pageSize=50");
        page1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await page1.Content.ReadFromJsonAsync<WorkoutTimeSeriesResponse>();
        body1!.Items.Should().HaveCount(50);
        body1.TotalCount.Should().Be(expectedSamples);
        body1.TotalPages.Should().Be(4);

        var page2 = await client.GetAsync($"/workouts/{workout.Id}/time-series?page=2&pageSize=50");
        page2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await page2.Content.ReadFromJsonAsync<WorkoutTimeSeriesResponse>();
        body2!.Items.Should().HaveCount(50);
        body2.Items[0].ElapsedSeconds.Should().BeGreaterThan(body1.Items[^1].ElapsedSeconds);

        var badPage = await client.GetAsync($"/workouts/{workout.Id}/time-series?page=999&pageSize=50");
        badPage.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetWorkoutTimeSeries_ClampsPageSize_ToMax()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, durationS: 60);
            await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(db, workout, intervalSeconds: 10, totalDurationS: 60);
        }

        var response = await client.GetAsync($"/workouts/{workout.Id}/time-series?pageSize=999999");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WorkoutTimeSeriesResponse>();
        body!.PageSize.Should().Be(5000);
    }

    [Fact]
    public async Task GetWorkoutTimeSeries_Returns401_WhenNotAuthenticated()
    {
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/workouts/{Guid.NewGuid()}/time-series");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class WorkoutTimeSeriesResponse
    {
        [JsonPropertyName("items")]
        public List<WorkoutTimeSeriesItem> Items { get; set; } = [];

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
    }

    private sealed class WorkoutTimeSeriesItem
    {
        [JsonPropertyName("elapsedSeconds")]
        public int ElapsedSeconds { get; set; }

        [JsonPropertyName("heartRateBpm")]
        public int HeartRateBpm { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
    }
}
