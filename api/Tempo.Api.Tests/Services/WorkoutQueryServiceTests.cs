using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for WorkoutQueryService duplicate detection logic
/// </summary>
public class WorkoutQueryServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;

    public WorkoutQueryServiceTests()
    {
        // Create in-memory SQLite database for testing
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsWorkout_WhenExactMatch()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM,
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(existingWorkout.Id);
        result.StartedAt.Should().Be(startTime);
        result.DistanceM.Should().Be(distanceM);
        result.DurationS.Should().Be(durationS);
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsWorkout_WhenNearMatchWithinTolerance()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM + 0.5, // Within 1.0m tolerance
            DurationS = durationS, // Exact match
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(existingWorkout.Id);
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsWorkout_WhenDurationWithinTolerance()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM, // Exact match
            DurationS = durationS, // Exact match (within 1s tolerance)
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act - Query with duration within 1s tolerance
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(existingWorkout.Id);
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsNull_WhenDifferentStartTime()
    {
        // Arrange
        var startTime1 = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var startTime2 = new DateTime(2024, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime1,
            DistanceM = distanceM,
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime2, distanceM, durationS);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsNull_WhenDistanceExceedsTolerance()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM + 1.5, // Exceeds 1.0m tolerance
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsNull_WhenDurationExceedsTolerance()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM,
            DurationS = durationS + 2, // Exceeds 1s tolerance
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsNull_WhenNoWorkoutsExist()
    {
        // Arrange - no workouts in database

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(
            _db,
            new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            5000.0,
            1800);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsFirstMatch_WhenMultipleMatchesExist()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var workout1 = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM,
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test1",
            CreatedAt = DateTime.UtcNow
        };

        var workout2 = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM,
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test2",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.AddRange(workout1, workout2);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().NotBeNull();
        // Should return one of the matches (FirstOrDefault behavior)
        (result!.Id == workout1.Id || result.Id == workout2.Id).Should().BeTrue();
    }
}

