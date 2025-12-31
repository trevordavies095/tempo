using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for WorkoutCropService
/// </summary>
public class WorkoutCropServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly ILogger<WorkoutCropService> _logger;
    private readonly WorkoutCropService _service;

    public WorkoutCropServiceTests()
    {
        // Create in-memory SQLite database for testing
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();

        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<WorkoutCropService>();
        _service = new WorkoutCropService(_db, _logger);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CropWorkoutAsync_CropFromStartOnly_ModifiesCorrectly()
    {
        // Arrange
        var originalDuration = 1800; // 30 minutes
        var startTrim = 300; // 5 minutes from start
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);
        
        var originalStartedAt = workout.StartedAt;
        var originalDistance = workout.DistanceM;

        // Act
        var result = await _service.CropWorkoutAsync(workout, startTrim, 0);

        // Assert
        result.Should().NotBeNull();
        result.DurationS.Should().Be(originalDuration - startTrim);
        result.StartedAt.Should().Be(originalStartedAt.AddSeconds(startTrim));
        result.DistanceM.Should().BeLessThan(originalDistance); // Distance should be reduced
    }

    [Fact]
    public async Task CropWorkoutAsync_CropFromEndOnly_ModifiesCorrectly()
    {
        // Arrange
        var originalDuration = 1800; // 30 minutes
        var endTrim = 300; // 5 minutes from end
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);
        
        var originalStartedAt = workout.StartedAt;
        var originalDistance = workout.DistanceM;

        // Act
        var result = await _service.CropWorkoutAsync(workout, 0, endTrim);

        // Assert
        result.Should().NotBeNull();
        result.DurationS.Should().Be(originalDuration - endTrim);
        result.StartedAt.Should().Be(originalStartedAt); // Start time unchanged
        result.DistanceM.Should().BeLessThan(originalDistance); // Distance should be reduced
    }

    [Fact]
    public async Task CropWorkoutAsync_CropFromBothEnds_ModifiesCorrectly()
    {
        // Arrange
        var originalDuration = 1800; // 30 minutes
        var startTrim = 300; // 5 minutes from start
        var endTrim = 200; // ~3.3 minutes from end
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);
        
        var originalStartedAt = workout.StartedAt;
        var originalDistance = workout.DistanceM;

        // Act
        var result = await _service.CropWorkoutAsync(workout, startTrim, endTrim);

        // Assert
        result.Should().NotBeNull();
        result.DurationS.Should().Be(originalDuration - startTrim - endTrim);
        result.StartedAt.Should().Be(originalStartedAt.AddSeconds(startTrim));
        result.DistanceM.Should().BeLessThan(originalDistance); // Distance should be reduced
    }

    [Fact]
    public async Task CropWorkoutAsync_RecalculatesDuration_Correctly()
    {
        // Arrange
        var originalDuration = 1800; // 30 minutes
        var startTrim = 300;
        var endTrim = 200;
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);

        // Act
        var result = await _service.CropWorkoutAsync(workout, startTrim, endTrim);

        // Assert
        result.DurationS.Should().Be(originalDuration - startTrim - endTrim);
    }

    [Fact]
    public async Task CropWorkoutAsync_RecalculatesDistance_Correctly()
    {
        // Arrange
        var originalDuration = 1800;
        var originalDistance = 5000.0;
        var startTrim = 300;
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: originalDistance, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);

        // Act
        var result = await _service.CropWorkoutAsync(workout, startTrim, 0);

        // Assert
        result.DistanceM.Should().BeLessThan(originalDistance);
        result.DistanceM.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CropWorkoutAsync_RecalculatesPace_Correctly()
    {
        // Arrange
        var originalDuration = 1800; // 30 minutes
        var originalDistance = 5000.0; // 5km
        var originalPace = (int)(originalDuration / (originalDistance / 1000.0)); // seconds per km
        var startTrim = 300;
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: originalDistance, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);

        // Act
        var result = await _service.CropWorkoutAsync(workout, startTrim, 0);

        // Assert
        result.AvgPaceS.Should().BeGreaterThan(0);
        // Pace should be recalculated based on new duration and distance
        if (result.DistanceM > 0 && result.DurationS > 0)
        {
            var expectedPace = (int)(result.DurationS / (result.DistanceM / 1000.0));
            result.AvgPaceS.Should().BeInRange(expectedPace - 10, expectedPace + 10); // Allow small rounding differences
        }
    }

    [Fact]
    public async Task CropWorkoutAsync_WithCropExceedingDuration_ThrowsException()
    {
        // Arrange
        var originalDuration = 1800;
        var startTrim = 1000;
        var endTrim = 1000; // Total trim exceeds duration
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);

        // Act & Assert
        var act = async () => await _service.CropWorkoutAsync(workout, startTrim, endTrim);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*shorter than*");
    }

    [Fact]
    public async Task CropWorkoutAsync_WithoutRoute_ThrowsException()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: 1800);
        // Don't add route

        // Act & Assert
        var act = async () => await _service.CropWorkoutAsync(workout, 100, 100);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no route data*");
    }

    [Fact]
    public async Task CropWorkoutAsync_WithResultingDurationLessThan10s_ThrowsException()
    {
        // Arrange
        var originalDuration = 1800;
        var startTrim = 1000;
        var endTrim = 800; // Leaves only 0 seconds (less than 10s minimum)
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);

        // Act & Assert
        var act = async () => await _service.CropWorkoutAsync(workout, startTrim, endTrim);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*shorter than*10*seconds*");
    }

    [Fact]
    public async Task CropWorkoutAsync_CropsTimeSeries_Correctly()
    {
        // Arrange
        var originalDuration = 1800;
        var startTrim = 300;
        var endTrim = 200;
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration, intervalSeconds: 10);
        
        var originalTimeSeriesCount = await _db.WorkoutTimeSeries.Where(ts => ts.WorkoutId == workout.Id).CountAsync();

        // Act
        await _service.CropWorkoutAsync(workout, startTrim, endTrim);

        // Assert
        var croppedTimeSeries = await _db.WorkoutTimeSeries
            .Where(ts => ts.WorkoutId == workout.Id)
            .OrderBy(ts => ts.ElapsedSeconds)
            .ToListAsync();
        
        croppedTimeSeries.Should().NotBeEmpty();
        croppedTimeSeries.Count.Should().BeLessThan(originalTimeSeriesCount);
        
        // All elapsed seconds should be reindexed (start from 0)
        croppedTimeSeries[0].ElapsedSeconds.Should().Be(0);
        
        // All elapsed seconds should be within the new duration
        var newDuration = originalDuration - startTrim - endTrim;
        croppedTimeSeries.All(ts => ts.ElapsedSeconds <= newDuration).Should().BeTrue();
    }

    [Fact]
    public async Task CropWorkoutAsync_CropsRoute_Correctly()
    {
        // Arrange
        var originalDuration = 1800;
        var startTrim = 300;
        var endTrim = 200;
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        
        // Create route with many coordinates to ensure cropping is visible
        var coordinates = new List<double[]>();
        for (int i = 0; i < 50; i++)
        {
            coordinates.Add(new[] { 0.0 + (i * 0.0001), 0.0 + (i * 0.0001) });
        }
        var route = await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout, coordinates);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);
        
        var originalRouteJson = route.RouteGeoJson;
        var originalRoute = JsonSerializer.Deserialize<JsonElement>(originalRouteJson);
        var originalCoordinates = originalRoute.GetProperty("coordinates").GetArrayLength();

        // Act
        await _service.CropWorkoutAsync(workout, startTrim, endTrim);

        // Assert
        // Reload workout to get updated route
        await _db.Entry(workout).ReloadAsync();
        var updatedRoute = await _db.WorkoutRoutes.FirstOrDefaultAsync(r => r.WorkoutId == workout.Id);
        updatedRoute.Should().NotBeNull();
        updatedRoute!.RouteGeoJson.Should().NotBeNullOrEmpty();
        
        var croppedRoute = JsonSerializer.Deserialize<JsonElement>(updatedRoute.RouteGeoJson);
        var croppedCoordinates = croppedRoute.GetProperty("coordinates").GetArrayLength();
        // After cropping 300s from start and 200s from end (500s total from 1800s), we should have fewer coordinates
        croppedCoordinates.Should().BeLessThan(originalCoordinates);
        croppedCoordinates.Should().BeGreaterThan(0);
        // With 50 original coordinates and cropping ~28% (500/1800), we should have roughly 35-40 coordinates remaining
        croppedCoordinates.Should().BeInRange(30, 45);
    }

    [Fact]
    public async Task CropWorkoutAsync_RecalculatesAggregates_Correctly()
    {
        // Arrange
        var originalDuration = 1800;
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration, includeHeartRate: true, includeCadence: true);
        
        // Set some initial values
        workout.MaxHeartRateBpm = 180;
        workout.MinHeartRateBpm = 120;
        workout.AvgHeartRateBpm = 150;
        workout.MaxCadenceRpm = 180;
        workout.AvgCadenceRpm = 170;
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.CropWorkoutAsync(workout, 300, 200);

        // Assert
        // Aggregates should be recalculated from cropped time series
        // Since we're cropping, the values may change
        result.MaxHeartRateBpm.Should().BeGreaterThan(0);
        result.AvgHeartRateBpm.Should().BeGreaterThan(0);
        result.MaxCadenceRpm.Should().BeGreaterThan(0);
        result.AvgCadenceRpm.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CropWorkoutAsync_RecalculatesElevationStats_WhenPresent()
    {
        // Arrange
        var originalDuration = 1800;
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);
        
        // Set initial elevation
        workout.MinElevM = 100.0;
        workout.MaxElevM = 200.0;
        workout.ElevGainM = 50.0;
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.CropWorkoutAsync(workout, 300, 200);

        // Assert
        // Elevation stats should be recalculated from cropped data
        if (result.MinElevM.HasValue)
        {
            result.MinElevM.Value.Should().BeGreaterThan(0);
        }
        if (result.MaxElevM.HasValue)
        {
            result.MaxElevM.Value.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task CropWorkoutAsync_UpdatesStartedAt_Correctly()
    {
        // Arrange
        var originalDuration = 1800;
        var startTrim = 300;
        var originalStartedAt = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, startedAt: originalStartedAt, distanceM: 5000.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);

        // Act
        var result = await _service.CropWorkoutAsync(workout, startTrim, 0);

        // Assert
        result.StartedAt.Should().Be(originalStartedAt.AddSeconds(startTrim));
    }

    [Fact]
    public async Task CropWorkoutAsync_WithMinimalRemainingDuration_Succeeds()
    {
        // Arrange
        var originalDuration = 20; // 20 seconds
        var startTrim = 5;
        var endTrim = 5; // Leaves 10 seconds (minimum)
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 100.0, durationS: originalDuration);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, totalDurationS: originalDuration);

        // Act
        var result = await _service.CropWorkoutAsync(workout, startTrim, endTrim);

        // Assert
        result.DurationS.Should().Be(10); // Exactly the minimum
    }
}

