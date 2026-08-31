using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
/// Integration tests for ListWorkouts endpoint
/// </summary>
[Collection("Integration Tests")]
public class ListWorkoutsTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public ListWorkoutsTests(TempoWebApplicationFactory factory)
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

    #region Pagination Tests

    [Fact]
    public async Task ListWorkouts_ReturnsDefaultPagination_WhenNoParameters()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Seed 25 workouts
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            for (int i = 0; i < 25; i++)
            {
                await TestDataSeeder.SeedWorkoutAsync(db, name: $"Workout {i}");
            }
        }

        // Act
        var response = await client.GetAsync("/workouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(20); // Default pageSize
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalCount.Should().Be(25);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task ListWorkouts_ReturnsCustomPageSize_WhenPageSizeSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Seed 15 workouts
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            for (int i = 0; i < 15; i++)
            {
                await TestDataSeeder.SeedWorkoutAsync(db, name: $"Workout {i}");
            }
        }

        // Act
        var response = await client.GetAsync("/workouts?pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(10);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().Be(15);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task ListWorkouts_ReturnsSecondPage_WhenPageSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Seed 25 workouts
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            for (int i = 0; i < 25; i++)
            {
                await TestDataSeeder.SeedWorkoutAsync(db, name: $"Workout {i}");
            }
        }

        // Act
        var response = await client.GetAsync("/workouts?page=2&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(10);
        result.Page.Should().Be(2);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task ListWorkouts_CalculatesTotalPagesCorrectly()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Seed 25 workouts
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            for (int i = 0; i < 25; i++)
            {
                await TestDataSeeder.SeedWorkoutAsync(db, name: $"Workout {i}");
            }
        }

        // Act
        var response = await client.GetAsync("/workouts?pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(25);
        result.TotalPages.Should().Be(3); // 25 / 10 = 2.5, rounded up = 3
    }

    [Fact]
    public async Task ListWorkouts_ReturnsNotFound_WhenInvalidPageNumber()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Seed 10 workouts
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            for (int i = 0; i < 10; i++)
            {
                await TestDataSeeder.SeedWorkoutAsync(db, name: $"Workout {i}");
            }
        }

        // Act
        var response = await client.GetAsync("/workouts?page=999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListWorkouts_CapsPageSizeAt100_WhenPageSizeExceeds100()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Seed 150 workouts
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            for (int i = 0; i < 150; i++)
            {
                await TestDataSeeder.SeedWorkoutAsync(db, name: $"Workout {i}");
            }
        }

        // Act
        var response = await client.GetAsync("/workouts?pageSize=200");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.PageSize.Should().Be(100); // Capped at 100
        result.Items.Should().HaveCount(100);
    }

    #endregion

    #region Date Filter Tests

    [Fact]
    public async Task ListWorkouts_FiltersByStartDate_WhenStartDateSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var cutoffDate = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Create workouts before and after cutoff
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: cutoffDate.AddDays(-5), name: "Before");
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: cutoffDate.AddDays(5), name: "After");
        }

        // Act
        var response = await client.GetAsync($"/workouts?startDate={cutoffDate:yyyy-MM-dd}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.Items[0].Should().NotBeNull();
    }

    [Fact]
    public async Task ListWorkouts_FiltersByEndDate_WhenEndDateSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var cutoffDate = new DateTime(2024, 6, 15, 23, 59, 59, DateTimeKind.Utc);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Create workouts before and after cutoff
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: cutoffDate.AddDays(-5), name: "Before");
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: cutoffDate.AddDays(5), name: "After");
        }

        // Act
        var response = await client.GetAsync($"/workouts?endDate={cutoffDate:yyyy-MM-dd}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListWorkouts_FiltersByDateRange_WhenBothDatesSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var startDate = new DateTime(2024, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2024, 6, 20, 23, 59, 59, DateTimeKind.Utc);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: startDate.AddDays(-5), name: "Before");
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: startDate.AddDays(5), name: "InRange");
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: endDate.AddDays(5), name: "After");
        }

        // Act
        var response = await client.GetAsync($"/workouts?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListWorkouts_NormalizesUtcDates_WhenLocalTimeProvided()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var utcDate = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: utcDate, name: "Test");
        }

        // Act - Provide date as local time (simulated)
        var response = await client.GetAsync($"/workouts?startDate={utcDate:yyyy-MM-dd}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListWorkouts_ReturnsEmptyResults_WhenNoMatchesDateRange()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var futureDate = DateTime.UtcNow.AddYears(1);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, name: "Past Workout");
        }

        // Act
        var response = await client.GetAsync($"/workouts?startDate={futureDate:yyyy-MM-dd}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    #endregion

    #region Distance Filter Tests

    [Fact]
    public async Task ListWorkouts_FiltersByMinDistance_WhenMinDistanceSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 3000, name: "Short");
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 5000, name: "Medium");
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 10000, name: "Long");
        }

        // Act
        var response = await client.GetAsync("/workouts?minDistanceM=5000");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ListWorkouts_FiltersByMaxDistance_WhenMaxDistanceSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 3000, name: "Short");
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 5000, name: "Medium");
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 10000, name: "Long");
        }

        // Act
        var response = await client.GetAsync("/workouts?maxDistanceM=5000");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ListWorkouts_FiltersByDistanceRange_WhenBothSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 3000, name: "Short");
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 5000, name: "Medium");
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 10000, name: "Long");
        }

        // Act
        var response = await client.GetAsync("/workouts?minDistanceM=4000&maxDistanceM=6000");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
    }

    #endregion

    #region Keyword Search Tests

    [Fact]
    public async Task ListWorkouts_SearchesNameField_WhenKeywordSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout1 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Morning Run");
            workout1.Source = "garmin";
            var workout2 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Evening Run");
            workout2.Source = "garmin";
            var workout3 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Lunch Walk");
            workout3.Source = "garmin";
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/workouts?keyword=Run");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ListWorkouts_SearchesDeviceField_WhenKeywordSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout1 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Workout 1");
            workout1.Device = "Garmin Forerunner 945";
            var workout2 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Workout 2");
            workout2.Device = "Apple Watch";
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/workouts?keyword=Garmin");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListWorkouts_SearchesSourceField_WhenKeywordSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout1 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Workout 1");
            workout1.Source = "garmin_import";
            var workout2 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Workout 2");
            workout2.Source = "strava_import";
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/workouts?keyword=strava");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListWorkouts_IsCaseInsensitive_WhenSearching()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, name: "Morning Run");
        }

        // Act
        var response = await client.GetAsync("/workouts?keyword=MORNING");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListWorkouts_SupportsPartialMatch_WhenSearching()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, name: "Morning Run");
        }

        // Act
        var response = await client.GetAsync("/workouts?keyword=orn");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
    }

    #endregion

    #region Run Type Filter Tests

    [Fact]
    public async Task ListWorkouts_FiltersByRunType_WhenRunTypeSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout1 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Race");
            workout1.RunType = "Race";
            var workout2 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Easy Run");
            workout2.RunType = "Easy Run";
            var workout3 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Workout");
            workout3.RunType = "Workout";
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/workouts?runType=Race");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListWorkouts_ReturnsEmpty_WhenRunTypeNoMatches()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(db, name: "Easy Run");
            workout.RunType = "Easy Run";
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/workouts?runType=Race");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    #endregion

    #region Sorting Tests

    [Fact]
    public async Task ListWorkouts_SortsByStartedAtDesc_ByDefault()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: DateTime.UtcNow.AddHours(-3), name: "Oldest");
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: DateTime.UtcNow.AddHours(-1), name: "Newest");
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: DateTime.UtcNow.AddHours(-2), name: "Middle");
        }

        // Act
        var response = await client.GetAsync("/workouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(3);
        // Should be sorted by startedAt desc (newest first)
    }

    [Fact]
    public async Task ListWorkouts_SortsByStartedAtAsc_WhenSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: DateTime.UtcNow.AddHours(-3), name: "Oldest");
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: DateTime.UtcNow.AddHours(-1), name: "Newest");
        }

        // Act
        var response = await client.GetAsync("/workouts?sortBy=startedAt&sortOrder=asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListWorkouts_SortsByName_WhenSortByName()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, name: "Zebra Run");
            await TestDataSeeder.SeedWorkoutAsync(db, name: "Alpha Run");
            await TestDataSeeder.SeedWorkoutAsync(db, name: "Beta Run");
        }

        // Act
        var response = await client.GetAsync("/workouts?sortBy=name&sortOrder=asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListWorkouts_SortsByDuration_WhenSortByDuration()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, durationS: 3600, name: "Long");
            await TestDataSeeder.SeedWorkoutAsync(db, durationS: 1800, name: "Medium");
            await TestDataSeeder.SeedWorkoutAsync(db, durationS: 900, name: "Short");
        }

        // Act
        var response = await client.GetAsync("/workouts?sortBy=duration&sortOrder=asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListWorkouts_SortsByDistance_WhenSortByDistance()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 10000, name: "Long");
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 5000, name: "Medium");
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 3000, name: "Short");
        }

        // Act
        var response = await client.GetAsync("/workouts?sortBy=distance&sortOrder=asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListWorkouts_SortsByElevation_WhenSortByElevation()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout1 = await TestDataSeeder.SeedWorkoutAsync(db, name: "High");
            workout1.ElevGainM = 500;
            var workout2 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Low");
            workout2.ElevGainM = 100;
            var workout3 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Medium");
            workout3.ElevGainM = 300;
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/workouts?sortBy=elevation&sortOrder=asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListWorkouts_SortsByRelativeEffort_WhenSortByRelativeEffort()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout1 = await TestDataSeeder.SeedWorkoutAsync(db, name: "High Effort");
            workout1.RelativeEffort = 200;
            var workout2 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Low Effort");
            workout2.RelativeEffort = 50;
            var workout3 = await TestDataSeeder.SeedWorkoutAsync(db, name: "Medium Effort");
            workout3.RelativeEffort = 100;
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/workouts?sortBy=relativeEffort&sortOrder=asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(3);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ListWorkouts_ReturnsEmptyResults_WhenNoWorkoutsMatchFilters()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, name: "Test");
        }

        // Act
        var response = await client.GetAsync("/workouts?keyword=Nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ListWorkouts_ReturnsEmptyResults_WhenNoWorkoutsInDatabase()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Act
        var response = await client.GetAsync("/workouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task ListWorkouts_AppliesAllFilters_WhenMultipleFiltersSpecified()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var startDate = DateTime.UtcNow.AddDays(-10);
        var endDate = DateTime.UtcNow.AddDays(-5);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: startDate.AddDays(2),
                distanceM: 5000,
                name: "Morning Run");
            workout.RunType = "Easy Run";
            await db.SaveChangesAsync();
            
            // Add workouts that don't match all filters
            await TestDataSeeder.SeedWorkoutAsync(db, startedAt: startDate.AddDays(-5), name: "Too Old");
            await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 2000, name: "Too Short");
        }

        // Act
        var response = await client.GetAsync(
            $"/workouts?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}&minDistanceM=4000&keyword=Morning&runType=Easy Run&sortBy=name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutsListResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
    }

    #endregion

    #region Route preview tests

    [Fact]
    public async Task ListWorkouts_ReturnsPreviewRoute_AfterImport()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var gpxContent = CreateGpxContent(pointCount: 150);
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "long-run.gpx"
        };
        formData.Add(fileContent);

        var importResponse = await client.PostAsync("/workouts/import", formData);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        int fullPointCount;
        int previewPointCount;
        Guid workoutId;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts.Include(w => w.Route).SingleAsync();
            workout.Route.Should().NotBeNull();
            workout.Route!.PreviewGeoJson.Should().NotBeNullOrEmpty();
            fullPointCount = CountLineStringPoints(workout.Route.RouteGeoJson);
            previewPointCount = CountLineStringPoints(workout.Route.PreviewGeoJson!);
            workoutId = workout.Id;
        }

        fullPointCount.Should().BeGreaterThan(100);
        previewPointCount.Should().BeLessThanOrEqualTo(100);
        previewPointCount.Should().BeLessThan(fullPointCount);

        var response = await client.GetAsync("/workouts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = payload.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("id").GetGuid().Should().Be(workoutId);
        item.GetProperty("hasRoute").GetBoolean().Should().BeTrue();
        var route = item.GetProperty("route");
        route.GetProperty("type").GetString().Should().Be("LineString");
        route.GetProperty("coordinates").GetArrayLength().Should().Be(previewPointCount);
        route.GetProperty("coordinates").GetArrayLength().Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task ListWorkouts_ReturnsFullRoute_WhenPreviewIsNull()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var coordinates = CreateWavyCoordinates(180);
        Guid workoutId;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(db, name: "Unbackfilled");
            var route = await TestDataSeeder.SeedWorkoutWithRouteAsync(db, workout, coordinates);
            route.PreviewGeoJson.Should().BeNull();
            workoutId = workout.Id;
        }

        var response = await client.GetAsync("/workouts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = payload.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("id").GetGuid().Should().Be(workoutId);
        item.GetProperty("hasRoute").GetBoolean().Should().BeTrue();
        item.GetProperty("route").GetProperty("type").GetString().Should().Be("LineString");
        item.GetProperty("route").GetProperty("coordinates").GetArrayLength().Should().Be(180);
    }

    [Fact]
    public async Task ListWorkouts_ReturnsFullRoute_WhenPreviewIsSentinel()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var coordinates = CreateWavyCoordinates(120);
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(db, name: "Sentinel preview");
            var route = await TestDataSeeder.SeedWorkoutWithRouteAsync(db, workout, coordinates);
            route.PreviewGeoJson = TrackGeometry.EmptyRoutePreviewSentinel;
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/workouts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = payload.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("hasRoute").GetBoolean().Should().BeTrue();
        item.GetProperty("route").GetProperty("coordinates").GetArrayLength().Should().Be(120);
    }

    #endregion

    #region Media and splitsCount tests

    [Fact]
    public async Task ListWorkouts_IncludesMediaInCreatedAtOrder_AndEmptyArrayWhenNone()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Guid withMediaId;
        Guid withoutMediaId;
        Guid earliestId;
        Guid middleId;
        Guid latestId;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var withMedia = await TestDataSeeder.SeedWorkoutAsync(
                db, startedAt: new DateTime(2024, 6, 2, 10, 0, 0, DateTimeKind.Utc), name: "With media");
            var withoutMedia = await TestDataSeeder.SeedWorkoutAsync(
                db, startedAt: new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc), name: "No media");
            withMediaId = withMedia.Id;
            withoutMediaId = withoutMedia.Id;

            var latest = new WorkoutMedia
            {
                WorkoutId = withMedia.Id,
                Filename = "latest.jpg",
                FilePath = "/tmp/latest.jpg",
                MimeType = "image/jpeg",
                FileSizeBytes = 100,
                Caption = "should not appear on list",
                CreatedAt = new DateTime(2024, 6, 2, 12, 0, 2, DateTimeKind.Utc)
            };
            var earliest = new WorkoutMedia
            {
                WorkoutId = withMedia.Id,
                Filename = "earliest.mp4",
                FilePath = "/tmp/earliest.mp4",
                MimeType = "video/mp4",
                FileSizeBytes = 200,
                CreatedAt = new DateTime(2024, 6, 2, 12, 0, 0, DateTimeKind.Utc)
            };
            var middle = new WorkoutMedia
            {
                WorkoutId = withMedia.Id,
                Filename = "middle.png",
                FilePath = "/tmp/middle.png",
                MimeType = "image/png",
                FileSizeBytes = 150,
                CreatedAt = new DateTime(2024, 6, 2, 12, 0, 1, DateTimeKind.Utc)
            };
            db.WorkoutMedia.AddRange(latest, earliest, middle);
            await db.SaveChangesAsync();
            earliestId = earliest.Id;
            middleId = middle.Id;
            latestId = latest.Id;
        }

        var response = await client.GetAsync("/workouts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);

        var withMediaItem = items.Single(i => i.GetProperty("id").GetGuid() == withMediaId);
        var media = withMediaItem.GetProperty("media").EnumerateArray().ToList();
        media.Should().HaveCount(3);
        media.Select(m => m.GetProperty("id").GetGuid()).Should().Equal(earliestId, middleId, latestId);
        media.Select(m => m.GetProperty("mimeType").GetString()).Should().Equal("video/mp4", "image/png", "image/jpeg");
        foreach (var mediaItem in media)
        {
            mediaItem.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "mimeType");
        }

        var withoutMediaItem = items.Single(i => i.GetProperty("id").GetGuid() == withoutMediaId);
        withoutMediaItem.GetProperty("media").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListWorkouts_ReturnsCorrectSplitsCount()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Guid withSplitsId;
        Guid withoutSplitsId;
        int expectedSplits;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var withSplits = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2024, 6, 2, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000,
                durationS: 1800,
                name: "Has splits");
            var splits = await TestDataSeeder.SeedWorkoutWithSplitsAsync(db, withSplits);
            expectedSplits = splits.Count;
            withSplitsId = withSplits.Id;

            var withoutSplits = await TestDataSeeder.SeedWorkoutAsync(
                db,
                startedAt: new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                name: "No splits");
            withoutSplitsId = withoutSplits.Id;
        }

        expectedSplits.Should().BeGreaterThan(0);

        var response = await client.GetAsync("/workouts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("items").EnumerateArray().ToList();

        items.Single(i => i.GetProperty("id").GetGuid() == withSplitsId)
            .GetProperty("splitsCount").GetInt32().Should().Be(expectedSplits);
        items.Single(i => i.GetProperty("id").GetGuid() == withoutSplitsId)
            .GetProperty("splitsCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ListWorkoutMedia_ResponseShapeUnchanged_WhenListedFromFeed()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Guid workoutId;
        Guid mediaId;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(db, name: "Media shape");
            workoutId = workout.Id;
            var media = new WorkoutMedia
            {
                WorkoutId = workout.Id,
                Filename = "photo.jpg",
                FilePath = "/tmp/photo.jpg",
                MimeType = "image/jpeg",
                FileSizeBytes = 2048,
                Caption = "finish line",
                CreatedAt = new DateTime(2024, 6, 2, 12, 0, 0, DateTimeKind.Utc)
            };
            db.WorkoutMedia.Add(media);
            await db.SaveChangesAsync();
            mediaId = media.Id;
        }

        var listResponse = await client.GetAsync("/workouts");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listPayload = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var listMedia = listPayload.GetProperty("items")[0].GetProperty("media")[0];
        listMedia.GetProperty("id").GetGuid().Should().Be(mediaId);
        listMedia.GetProperty("mimeType").GetString().Should().Be("image/jpeg");
        listMedia.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "mimeType");

        var mediaResponse = await client.GetAsync($"/workouts/{workoutId}/media");
        mediaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var mediaPayload = await mediaResponse.Content.ReadFromJsonAsync<JsonElement>();
        var detail = mediaPayload.EnumerateArray().Single();
        detail.GetProperty("id").GetGuid().Should().Be(mediaId);
        detail.GetProperty("filename").GetString().Should().Be("photo.jpg");
        detail.GetProperty("mimeType").GetString().Should().Be("image/jpeg");
        detail.GetProperty("fileSizeBytes").GetInt64().Should().Be(2048);
        detail.GetProperty("caption").GetString().Should().Be("finish line");
        detail.TryGetProperty("createdAt", out _).Should().BeTrue();
        detail.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "id", "filename", "mimeType", "fileSizeBytes", "caption", "createdAt");
    }

    private static List<double[]> CreateWavyCoordinates(int count)
    {
        var coordinates = new List<double[]>(count);
        for (var i = 0; i < count; i++)
        {
            coordinates.Add(new[]
            {
                -122.4194 + i * 0.0008,
                37.7749 + Math.Sin(i / 2.5) * 0.012
            });
        }

        return coordinates;
    }

    private static string CreateGpxContent(int pointCount)
    {
        var start = new DateTime(2024, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var points = new StringBuilder();
        for (var i = 0; i < pointCount; i++)
        {
            var progress = (double)i / (pointCount - 1);
            var lat = 37.7749 + progress * 0.12;
            var lon = -122.4194 + progress * 0.12;
            var time = start.AddSeconds(progress * 3600);
            points.AppendLine($@"      <trkpt lat=""{lat:F6}"" lon=""{lon:F6}"">
        <ele>10.0</ele>
        <time>{time:yyyy-MM-ddTHH:mm:ss}Z</time>
      </trkpt>");
        }

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <name>Long Run</name>
    <trkseg>
{points}
    </trkseg>
  </trk>
</gpx>";
    }

    private static int CountLineStringPoints(string geoJson)
    {
        using var doc = JsonDocument.Parse(geoJson);
        return doc.RootElement.GetProperty("coordinates").GetArrayLength();
    }

    #endregion

    private class WorkoutsListResponse
    {
        public List<object> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }
}

