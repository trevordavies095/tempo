using System.Net;
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
/// Integration tests for GetWorkout, UpdateWorkout, and DeleteWorkout endpoints
/// </summary>
[Collection("Integration Tests")]
public class WorkoutDetailsUpdateDeleteTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public WorkoutDetailsUpdateDeleteTests(TempoWebApplicationFactory factory)
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
            // Use a transaction to ensure atomic cleanup
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                // Delete in order to respect foreign key constraints
                await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutTimeSeries");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutSplits");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutMedia");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM BestEfforts");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutRoutes");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Workouts");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM UserSettings");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Shoes");
                
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    #region GetWorkout Tests

    [Fact]
    public async Task GetWorkout_ReturnsCompleteWorkout_WithAllNestedData()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Create shoe
            shoe = await TestDataSeeder.SeedShoeAsync(db, brand: "Nike", model: "Pegasus");
            
            // Create complete workout with all related data
            workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
                db,
                shoeId: shoe.Id,
                name: "Complete Test Run",
                distanceM: 5000,
                durationS: 1800);
            
            // Add weather data
            workout.Weather = JsonSerializer.Serialize(new
            {
                temperature = 20.0,
                humidity = 60.0,
                precipitation = 0.0
            });
            
            // Add raw GPX data
            workout.RawGpxData = JsonSerializer.Serialize(new
            {
                metadata = new { name = "Test Run" },
                trackPoints = new[] { new { lat = 0.0, lon = 0.0 } }
            });
            
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync($"/workouts/{workout.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutDetailResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(workout.Id);
        result.Name.Should().Be("Complete Test Run");
        result.DistanceM.Should().Be(5000);
        result.DurationS.Should().Be(1800);
        
        // Verify nested data
        result.Route.Should().NotBeNull();
        result.Splits.Should().NotBeEmpty();
        result.Shoe.Should().NotBeNull();
        result.Shoe!.Id.Should().Be(shoe.Id);
        result.Shoe.Brand.Should().Be("Nike");
        result.Shoe.Model.Should().Be("Pegasus");
        result.Weather.Should().NotBeNull();
        result.RawGpxData.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWorkout_Returns404_WhenWorkoutNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/workouts/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task GetWorkout_HandlesNullAndEmptyData_WhenWorkoutHasNoRelatedData()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, name: "Minimal Workout");
        }

        // Act
        var response = await client.GetAsync($"/workouts/{workout.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutDetailResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(workout.Id);
        result.Name.Should().Be("Minimal Workout");
        
        // Verify null/empty handling
        result.Route.Should().BeNull();
        result.Splits.Should().BeEmpty();
        result.Shoe.Should().BeNull();
        result.Weather.Should().BeNull();
        result.RawGpxData.Should().BeNull();
        result.RawFitData.Should().BeNull();
        result.RawStravaData.Should().BeNull();
    }

    [Fact]
    public async Task GetWorkout_SerializesAllFields_WhenWorkoutHasAllFields()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(
                db,
                name: "Full Field Test",
                distanceM: 10000,
                durationS: 3600);
            
            // Set all optional fields
            workout.RunType = "Race";
            workout.Notes = "Test notes";
            workout.Source = "garmin";
            workout.Device = "Garmin Forerunner 945";
            workout.ElevGainM = 100.5;
            workout.ElevLossM = 95.2;
            workout.MinElevM = 50.0;
            workout.MaxElevM = 150.5;
            workout.MaxSpeedMps = 5.5;
            workout.AvgSpeedMps = 2.78;
            workout.MovingTimeS = 3500;
            workout.MaxHeartRateBpm = 180;
            workout.AvgHeartRateBpm = 150;
            workout.MinHeartRateBpm = 120;
            workout.MaxCadenceRpm = 180;
            workout.AvgCadenceRpm = 170;
            workout.MaxPowerWatts = 300;
            workout.AvgPowerWatts = 250;
            workout.Calories = 500;
            workout.RelativeEffort = 150;
            
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync($"/workouts/{workout.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkoutDetailResponse>();
        result.Should().NotBeNull();
        
        // Verify all fields are serialized
        result!.RunType.Should().Be("Race");
        result.Notes.Should().Be("Test notes");
        result.Source.Should().Be("garmin");
        result.Device.Should().Be("Garmin Forerunner 945");
        result.ElevGainM.Should().Be(100.5);
        result.ElevLossM.Should().Be(95.2);
        result.MinElevM.Should().Be(50.0);
        result.MaxElevM.Should().Be(150.5);
        result.MaxSpeedMps.Should().Be(5.5);
        result.AvgSpeedMps.Should().Be(2.78);
        result.MovingTimeS.Should().Be(3500);
        result.MaxHeartRateBpm.Should().Be(180);
        result.AvgHeartRateBpm.Should().Be(150);
        result.MinHeartRateBpm.Should().Be(120);
        result.MaxCadenceRpm.Should().Be(180);
        result.AvgCadenceRpm.Should().Be(170);
        result.MaxPowerWatts.Should().Be(300);
        result.AvgPowerWatts.Should().Be(250);
        result.Calories.Should().Be(500);
        result.RelativeEffort.Should().Be(150);
    }

    #endregion

    #region UpdateWorkout Tests

    [Fact]
    public async Task UpdateWorkout_UpdatesName_WhenNameProvided()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, name: "Original Name");
        }

        var updateRequest = new { name = "Updated Name" };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateWorkoutResponse>();
        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Name");
        
        // Verify in database
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var updatedWorkout = await db.Workouts.FindAsync(workout.Id);
            updatedWorkout.Should().NotBeNull();
            updatedWorkout!.Name.Should().Be("Updated Name");
            // Verify other fields unchanged
            updatedWorkout.DistanceM.Should().Be(workout.DistanceM);
            updatedWorkout.DurationS.Should().Be(workout.DurationS);
        }
    }

    [Fact]
    public async Task UpdateWorkout_UpdatesRunType_WhenRunTypeProvided()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            workout.RunType = "Easy Run";
            await db.SaveChangesAsync();
        }

        var updateRequest = new { runType = "Race" };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateWorkoutResponse>();
        result.Should().NotBeNull();
        result!.RunType.Should().Be("Race");
        
        // Verify in database
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var updatedWorkout = await db.Workouts.FindAsync(workout.Id);
            updatedWorkout!.RunType.Should().Be("Race");
        }
    }

    [Fact]
    public async Task UpdateWorkout_UpdatesNotes_WhenNotesProvided()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        var updateRequest = new { notes = "Test notes for workout" };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateWorkoutResponse>();
        result.Should().NotBeNull();
        result!.Notes.Should().Be("Test notes for workout");
        
        // Verify in database
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var updatedWorkout = await db.Workouts.FindAsync(workout.Id);
            updatedWorkout!.Notes.Should().Be("Test notes for workout");
        }
    }

    [Fact]
    public async Task UpdateWorkout_AssignsShoe_WhenShoeIdProvided()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            shoe = await TestDataSeeder.SeedShoeAsync(db, brand: "Nike", model: "Pegasus");
        }

        var updateRequest = new { shoeId = shoe.Id.ToString() };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateWorkoutResponse>();
        result.Should().NotBeNull();
        result!.ShoeId.Should().Be(shoe.Id.ToString());
        
        // Verify in database
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var updatedWorkout = await db.Workouts.FindAsync(workout.Id);
            updatedWorkout!.ShoeId.Should().Be(shoe.Id);
        }
    }

    [Fact]
    public async Task UpdateWorkout_RemovesShoeAssignment_WhenShoeIdIsNull()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db);
            workout = await TestDataSeeder.SeedWorkoutAsync(db, shoeId: shoe.Id);
        }

        var updateRequest = new { shoeId = (string?)null };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateWorkoutResponse>();
        result.Should().NotBeNull();
        result!.ShoeId.Should().BeNull();
        
        // Verify in database
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var updatedWorkout = await db.Workouts.FindAsync(workout.Id);
            updatedWorkout!.ShoeId.Should().BeNull();
        }
    }

    [Fact]
    public async Task UpdateWorkout_UpdatesMultipleFields_WhenMultipleFieldsProvided()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, name: "Original");
            workout.RunType = "Easy Run";
            workout.Notes = "Original notes";
            await db.SaveChangesAsync();
        }

        var updateRequest = new
        {
            name = "Updated Name",
            runType = "Workout",
            notes = "Updated notes"
        };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateWorkoutResponse>();
        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Name");
        result.RunType.Should().Be("Workout");
        result.Notes.Should().Be("Updated notes");
        
        // Verify other fields unchanged
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var updatedWorkout = await db.Workouts.FindAsync(workout.Id);
            updatedWorkout!.DistanceM.Should().Be(workout.DistanceM);
            updatedWorkout.DurationS.Should().Be(workout.DurationS);
        }
    }

    [Fact]
    public async Task UpdateWorkout_Returns400_WhenInvalidRunType()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        var updateRequest = new { runType = "Invalid" };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("Invalid runType");
    }

    [Fact]
    public async Task UpdateWorkout_AcceptsValidRunTypes()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var validRunTypes = new[] { "Race", "Workout", "Long Run", "Easy Run" };
        
        foreach (var runType in validRunTypes)
        {
            await EnsureCleanDatabaseAsync();
            
            Workout workout;
            using (var scope = _factory.Server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
                workout = await TestDataSeeder.SeedWorkoutAsync(db);
            }

            var updateRequest = new { runType };
            var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

            // Act
            var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<UpdateWorkoutResponse>();
            result.Should().NotBeNull();
            result!.RunType.Should().Be(runType);
        }
    }

    [Fact]
    public async Task UpdateWorkout_AcceptsNullRunType()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            workout.RunType = "Race";
            await db.SaveChangesAsync();
        }

        var updateRequest = new { runType = (string?)null };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateWorkoutResponse>();
        result.Should().NotBeNull();
        result!.RunType.Should().BeNull();
    }

    [Fact]
    public async Task UpdateWorkout_Returns400_WhenShoeNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        var nonExistentShoeId = Guid.NewGuid();
        var updateRequest = new { shoeId = nonExistentShoeId.ToString() };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("Shoe not found");
    }

    [Fact]
    public async Task UpdateWorkout_Returns400_WhenInvalidGuidFormat()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        var updateRequest = new { shoeId = "invalid-guid" };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("valid GUID");
    }

    [Fact]
    public async Task UpdateWorkout_Returns404_WhenWorkoutNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentId = Guid.NewGuid();

        var updateRequest = new { name = "Updated" };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{nonExistentId}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateWorkout_PersistsChanges_ToDatabase()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, name: "Original");
        }

        var updateRequest = new { name = "Persisted Name", runType = "Race" };
        var content = new StringContent(JsonSerializer.Serialize(updateRequest), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/workouts/{workout.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify persistence with fresh database context
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var persistedWorkout = await db.Workouts.FindAsync(workout.Id);
            persistedWorkout.Should().NotBeNull();
            persistedWorkout!.Name.Should().Be("Persisted Name");
            persistedWorkout.RunType.Should().Be("Race");
        }
    }

    #endregion

    #region DeleteWorkout Tests

    [Fact]
    public async Task DeleteWorkout_DeletesRoute_WhenWorkoutHasRoute()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        WorkoutRoute route;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            route = await TestDataSeeder.SeedWorkoutWithRouteAsync(db, workout);
        }

        // Act
        var response = await client.DeleteAsync($"/workouts/{workout.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify route deleted
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var deletedRoute = await db.WorkoutRoutes.FindAsync(route.Id);
            deletedRoute.Should().BeNull();
        }
    }

    [Fact]
    public async Task DeleteWorkout_DeletesSplits_WhenWorkoutHasSplits()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        List<WorkoutSplit> splits;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 5000);
            splits = await TestDataSeeder.SeedWorkoutWithSplitsAsync(db, workout);
        }

        // Act
        var response = await client.DeleteAsync($"/workouts/{workout.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify splits deleted
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var remainingSplits = await db.WorkoutSplits
                .Where(s => s.WorkoutId == workout.Id)
                .ToListAsync();
            remainingSplits.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task DeleteWorkout_DeletesMediaFiles_WhenWorkoutHasMedia()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        List<WorkoutMedia> media;
        string mediaDirectory;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            
            // Get media directory from factory
            var mediaConfig = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
            mediaDirectory = mediaConfig.RootPath;
            
            media = await TestDataSeeder.SeedWorkoutWithMediaAsync(db, workout, mediaDirectory, count: 2);
            
            // Verify files exist before deletion
            media.All(m => File.Exists(m.FilePath)).Should().BeTrue();
        }

        // Act
        var response = await client.DeleteAsync($"/workouts/{workout.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify media records deleted from database
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var remainingMedia = await db.WorkoutMedia
                .Where(m => m.WorkoutId == workout.Id)
                .ToListAsync();
            remainingMedia.Should().BeEmpty();
        }
        
        // Verify media files deleted from filesystem
        foreach (var mediaFile in media)
        {
            File.Exists(mediaFile.FilePath).Should().BeFalse();
        }
        
        // Verify media directory deleted
        var workoutMediaDir = Path.Combine(mediaDirectory, workout.Id.ToString());
        Directory.Exists(workoutMediaDir).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteWorkout_DeletesTimeSeries_WhenWorkoutHasTimeSeries()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        List<WorkoutTimeSeries> timeSeries;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, durationS: 1800);
            timeSeries = await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(db, workout);
        }

        // Act
        var response = await client.DeleteAsync($"/workouts/{workout.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify time series deleted
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var remainingTimeSeries = await db.WorkoutTimeSeries
                .Where(ts => ts.WorkoutId == workout.Id)
                .ToListAsync();
            remainingTimeSeries.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task DeleteWorkout_DeletesBestEfforts_WhenWorkoutHasBestEfforts()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        BestEffort bestEffort;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 5000);
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, workout);
            
            // Create best effort referencing this workout
            bestEffort = new BestEffort
            {
                Distance = "5K",
                DistanceM = 5000,
                TimeS = 1200,
                WorkoutId = workout.Id,
                WorkoutDate = workout.StartedAt,
                CalculatedAt = DateTime.UtcNow
            };
            db.BestEfforts.Add(bestEffort);
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.DeleteAsync($"/workouts/{workout.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify best effort deleted (cascade delete)
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var remainingBestEffort = await db.BestEfforts.FindAsync(bestEffort.Id);
            remainingBestEffort.Should().BeNull();
        }
    }

    [Fact]
    public async Task DeleteWorkout_UpdatesShoeMileage_WhenWorkoutWasAssignedToShoe()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        Shoe shoe;
        double initialMileage;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Create shoe with initial mileage
            shoe = await TestDataSeeder.SeedShoeAsync(db, initialMileage: 10000);
            
            // Create workout assigned to shoe
            workout = await TestDataSeeder.SeedWorkoutAsync(db, shoeId: shoe.Id, distanceM: 5000);
            
            // Get initial mileage
            var mileageService = scope.ServiceProvider.GetRequiredService<ShoeMileageService>();
            initialMileage = await mileageService.GetTotalMileageAsync(db, shoe.Id, "metric");
        }

        // Act
        var response = await client.DeleteAsync($"/workouts/{workout.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify shoe mileage updated (decreased)
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var mileageService = scope.ServiceProvider.GetRequiredService<ShoeMileageService>();
            var updatedMileage = await mileageService.GetTotalMileageAsync(db, shoe.Id, "metric");
            
            // Mileage should decrease by workout distance (5km)
            updatedMileage.Should().BeApproximately(initialMileage - 5.0, 0.01);
        }
    }

    [Fact]
    public async Task DeleteWorkout_Returns404_WhenWorkoutNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/workouts/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task DeleteWorkout_MaintainsDatabaseConsistency_WhenDeletingWorkoutWithAllRelatedData()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workoutToDelete;
        Workout otherWorkout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Create workout with all related data
            workoutToDelete = await TestDataSeeder.SeedWorkoutCompleteAsync(db, name: "To Delete");
            await TestDataSeeder.SeedWorkoutWithMediaAsync(
                db,
                workoutToDelete,
                scope.ServiceProvider.GetRequiredService<MediaStorageConfig>().RootPath);
            
            // Create another workout to verify it's unaffected
            otherWorkout = await TestDataSeeder.SeedWorkoutAsync(db, name: "To Keep");
        }

        // Act
        var response = await client.DeleteAsync($"/workouts/{workoutToDelete.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify deleted workout and all related data removed
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Workout deleted
            var deletedWorkout = await db.Workouts.FindAsync(workoutToDelete.Id);
            deletedWorkout.Should().BeNull();
            
            // Route deleted
            var route = await db.WorkoutRoutes.FirstOrDefaultAsync(r => r.WorkoutId == workoutToDelete.Id);
            route.Should().BeNull();
            
            // Splits deleted
            var splits = await db.WorkoutSplits.Where(s => s.WorkoutId == workoutToDelete.Id).ToListAsync();
            splits.Should().BeEmpty();
            
            // Time series deleted
            var timeSeries = await db.WorkoutTimeSeries.Where(ts => ts.WorkoutId == workoutToDelete.Id).ToListAsync();
            timeSeries.Should().BeEmpty();
            
            // Media deleted
            var media = await db.WorkoutMedia.Where(m => m.WorkoutId == workoutToDelete.Id).ToListAsync();
            media.Should().BeEmpty();
            
            // Other workout unaffected
            var keptWorkout = await db.Workouts.FindAsync(otherWorkout.Id);
            keptWorkout.Should().NotBeNull();
            keptWorkout!.Name.Should().Be("To Keep");
        }
    }

    #endregion

    #region Response Models

    private class WorkoutDetailResponse
    {
        public Guid Id { get; set; }
        public DateTime StartedAt { get; set; }
        public int DurationS { get; set; }
        public double DistanceM { get; set; }
        public int AvgPaceS { get; set; }
        public double? ElevGainM { get; set; }
        public double? ElevLossM { get; set; }
        public double? MinElevM { get; set; }
        public double? MaxElevM { get; set; }
        public double? MaxSpeedMps { get; set; }
        public double? AvgSpeedMps { get; set; }
        public int? MovingTimeS { get; set; }
        public byte? MaxHeartRateBpm { get; set; }
        public byte? AvgHeartRateBpm { get; set; }
        public byte? MinHeartRateBpm { get; set; }
        public byte? MaxCadenceRpm { get; set; }
        public byte? AvgCadenceRpm { get; set; }
        public ushort? MaxPowerWatts { get; set; }
        public ushort? AvgPowerWatts { get; set; }
        public ushort? Calories { get; set; }
        public int? RelativeEffort { get; set; }
        public string? RunType { get; set; }
        public string? Notes { get; set; }
        public string? Source { get; set; }
        public string? Device { get; set; }
        public string? Name { get; set; }
        public string? ShoeId { get; set; }
        public ShoeResponse? Shoe { get; set; }
        public object? Weather { get; set; }
        public object? RawGpxData { get; set; }
        public object? RawFitData { get; set; }
        public object? RawStravaData { get; set; }
        public DateTime CreatedAt { get; set; }
        public object? Route { get; set; }
        public List<SplitResponse> Splits { get; set; } = new();
    }

    private class SplitResponse
    {
        public int Idx { get; set; }
        public double DistanceM { get; set; }
        public int DurationS { get; set; }
        public int PaceS { get; set; }
    }

    private class ShoeResponse
    {
        public Guid Id { get; set; }
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    private class UpdateWorkoutResponse
    {
        public Guid Id { get; set; }
        public string? RunType { get; set; }
        public string? Notes { get; set; }
        public string? Name { get; set; }
        public string? ShoeId { get; set; }
    }

    private class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
    }

    #endregion
}

