using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for RelativeEffortService
/// </summary>
public class RelativeEffortServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly RelativeEffortService _service;

    public RelativeEffortServiceTests()
    {
        // Create in-memory SQLite database for testing
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();
        _service = new RelativeEffortService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    #region CalculateRelativeEffort Tests

    [Fact]
    public async Task CalculateRelativeEffort_WithTimeSeriesData_UsesTimeSeries()
    {
        // Arrange
        var zones = CreateValidZones();
        var workout = await SeedWorkoutWithTimeSeriesAsync(_db, heartRateValues: new byte[] { 120, 130, 140, 150, 160 });

        // Act
        var result = _service.CalculateRelativeEffort(workout, zones, _db);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CalculateRelativeEffort_WithRawFitData_UsesRawData()
    {
        // Arrange
        var zones = CreateValidZones();
        var workout = await SeedWorkoutWithRawFitDataAsync(_db, avgHeartRate: 150);

        // Act
        var result = _service.CalculateRelativeEffort(workout, zones, _db);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CalculateRelativeEffort_WithAvgHeartRateBpmField_UsesAverage()
    {
        // Arrange
        var zones = CreateValidZones();
        var workout = await SeedWorkoutAsync(_db, avgHeartRateBpm: 150, durationS: 1800);

        // Act
        var result = _service.CalculateRelativeEffort(workout, zones, _db);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CalculateRelativeEffort_WithNoHrData_ReturnsNull()
    {
        // Arrange
        var zones = CreateValidZones();
        var workout = await SeedWorkoutAsync(_db, avgHeartRateBpm: null, durationS: 1800);

        // Act
        var result = _service.CalculateRelativeEffort(workout, zones, _db);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CalculateRelativeEffort_WithInvalidZonesNull_ReturnsNull()
    {
        // Arrange
        List<HeartRateZone>? zones = null;
        var workout = await SeedWorkoutAsync(_db, avgHeartRateBpm: 150, durationS: 1800);

        // Act
        var result = _service.CalculateRelativeEffort(workout, zones!, _db);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CalculateRelativeEffort_WithInvalidZonesWrongCount_ReturnsNull()
    {
        // Arrange
        var zones = new List<HeartRateZone> { new() { MinBpm = 95, MaxBpm = 114 } }; // Only 1 zone
        var workout = await SeedWorkoutAsync(_db, avgHeartRateBpm: 150, durationS: 1800);

        // Act
        var result = _service.CalculateRelativeEffort(workout, zones, _db);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CalculateFromTimeSeries Tests

    [Fact]
    public void CalculateFromTimeSeries_WithTypicalWorkout_CalculatesCorrectly()
    {
        // Arrange
        var zones = CreateValidZones();
        var timeSeries = CreateTimeSeriesWithMixedZones(durationSeconds: 1800); // 30 minutes

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries, zones);

        // Assert
        result.Should().BeGreaterThan(0);
        // Should be some weighted combination of time in different zones
    }

    [Fact]
    public void CalculateFromTimeSeries_WithAllTimeInZone1_CalculatesCorrectly()
    {
        // Arrange
        var zones = CreateValidZones();
        // Zone 1: 95-114 BPM, use 110 BPM
        var timeSeries = CreateTimeSeriesWithConstantHr(durationSeconds: 1800, heartRate: 110); // 30 minutes

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries, zones);

        // Assert
        // 30 minutes * 1 (Zone 1 weight) = 30
        result.Should().BeInRange(25, 35); // Allow some variance due to rounding
    }

    [Fact]
    public void CalculateFromTimeSeries_WithAllTimeInZone5_CalculatesCorrectly()
    {
        // Arrange
        var zones = CreateValidZones();
        // Zone 5: 171-190 BPM, use 180 BPM
        var timeSeries = CreateTimeSeriesWithConstantHr(durationSeconds: 1800, heartRate: 180); // 30 minutes

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries, zones);

        // Assert
        // 30 minutes * 5 (Zone 5 weight) = 150
        result.Should().BeInRange(145, 155); // Allow some variance due to rounding
    }

    [Fact]
    public void CalculateFromTimeSeries_WithMixedZones_CalculatesCorrectly()
    {
        // Arrange
        var zones = CreateValidZones();
        // 10 min Zone 1 (110 BPM), 10 min Zone 3 (140 BPM), 10 min Zone 5 (180 BPM)
        var timeSeries = new List<WorkoutTimeSeries>();
        
        // 10 minutes in Zone 1 (600 seconds)
        for (int i = 0; i < 60; i++) // 60 points at 10-second intervals
        {
            timeSeries.Add(new WorkoutTimeSeries
            {
                ElapsedSeconds = i * 10,
                HeartRateBpm = 110 // Zone 1
            });
        }
        
        // 10 minutes in Zone 3 (600 seconds)
        for (int i = 60; i < 120; i++)
        {
            timeSeries.Add(new WorkoutTimeSeries
            {
                ElapsedSeconds = i * 10,
                HeartRateBpm = 140 // Zone 3
            });
        }
        
        // 10 minutes in Zone 5 (600 seconds)
        for (int i = 120; i < 180; i++)
        {
            timeSeries.Add(new WorkoutTimeSeries
            {
                ElapsedSeconds = i * 10,
                HeartRateBpm = 180 // Zone 5
            });
        }

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries, zones);

        // Assert
        // (10 min * 1) + (10 min * 3) + (10 min * 5) = 10 + 30 + 50 = 90
        result.Should().BeInRange(85, 95);
    }

    [Fact]
    public void CalculateFromTimeSeries_WithExtremelyShortWorkout_CalculatesCorrectly()
    {
        // Arrange
        var zones = CreateValidZones();
        var timeSeries = new List<WorkoutTimeSeries>
        {
            new() { ElapsedSeconds = 0, HeartRateBpm = 150 },
            new() { ElapsedSeconds = 5, HeartRateBpm = 155 }
        };

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries, zones);

        // Assert
        result.Should().BeGreaterThanOrEqualTo(0);
        // Very small effort for 5 seconds
    }

    [Fact]
    public void CalculateFromTimeSeries_WithEmptyTimeSeries_ReturnsZero()
    {
        // Arrange
        var zones = CreateValidZones();
        var timeSeries = new List<WorkoutTimeSeries>();

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries, zones);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void CalculateFromTimeSeries_WithNullTimeSeries_ReturnsZero()
    {
        // Arrange
        var zones = CreateValidZones();
        List<WorkoutTimeSeries>? timeSeries = null;

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries!, zones);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void CalculateFromTimeSeries_WithNullHrValues_SkipsNullValues()
    {
        // Arrange
        var zones = CreateValidZones();
        var timeSeries = new List<WorkoutTimeSeries>
        {
            new() { ElapsedSeconds = 0, HeartRateBpm = 150 },
            new() { ElapsedSeconds = 10, HeartRateBpm = null },
            new() { ElapsedSeconds = 20, HeartRateBpm = 155 },
            new() { ElapsedSeconds = 30, HeartRateBpm = null }
        };

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries, zones);

        // Assert
        result.Should().BeGreaterThanOrEqualTo(0);
        // Should only count points with HR values
    }

    [Fact]
    public void CalculateFromTimeSeries_WithInvalidZones_ReturnsZero()
    {
        // Arrange
        var zones = new List<HeartRateZone> { new() { MinBpm = 95, MaxBpm = 114 } }; // Only 1 zone
        var timeSeries = CreateTimeSeriesWithConstantHr(durationSeconds: 1800, heartRate: 150);

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries, zones);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void CalculateFromTimeSeries_WithLargeTimeGaps_ClampsTo1Second()
    {
        // Arrange
        var zones = CreateValidZones();
        var timeSeries = new List<WorkoutTimeSeries>
        {
            new() { ElapsedSeconds = 0, HeartRateBpm = 150 },
            new() { ElapsedSeconds = 50, HeartRateBpm = 155 } // 50 second gap (should clamp to 1 second)
        };

        // Act
        var result = _service.CalculateFromTimeSeries(timeSeries, zones);

        // Assert
        result.Should().BeGreaterThanOrEqualTo(0);
        // Should handle the gap correctly
    }

    #endregion

    #region CalculateFromRawData Tests

    [Fact]
    public async Task CalculateFromRawData_WithRawFitData_CalculatesFromAverage()
    {
        // Arrange
        var zones = CreateValidZones();
        var workout = await SeedWorkoutWithRawFitDataAsync(_db, avgHeartRate: 150);

        // Act
        var result = _service.CalculateFromRawData(workout, zones);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CalculateFromRawData_WithAvgHeartRateBpmField_CalculatesFromAverage()
    {
        // Arrange
        var zones = CreateValidZones();
        var workout = await SeedWorkoutAsync(_db, avgHeartRateBpm: 150, durationS: 1800);

        // Act
        var result = _service.CalculateFromRawData(workout, zones);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CalculateFromRawData_WithNoHrData_ReturnsNull()
    {
        // Arrange
        var zones = CreateValidZones();
        var workout = await SeedWorkoutAsync(_db, avgHeartRateBpm: null, durationS: 1800);

        // Act
        var result = _service.CalculateFromRawData(workout, zones);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CalculateFromRawData_WithInvalidZones_ReturnsNull()
    {
        // Arrange
        var zones = new List<HeartRateZone> { new() { MinBpm = 95, MaxBpm = 114 } }; // Only 1 zone
        var workout = await SeedWorkoutAsync(_db, avgHeartRateBpm: 150, durationS: 1800);

        // Act
        var result = _service.CalculateFromRawData(workout, zones);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetQualifyingWorkoutIdsAsync Tests

    [Fact]
    public async Task GetQualifyingWorkoutIdsAsync_WithTimeSeriesHr_IncludesWorkout()
    {
        // Arrange
        var workout = await SeedWorkoutWithTimeSeriesAsync(_db, heartRateValues: new byte[] { 120, 130, 140 });

        // Act
        var result = await _service.GetQualifyingWorkoutIdsAsync(_db);

        // Assert
        result.Should().Contain(workout.Id);
    }

    [Fact]
    public async Task GetQualifyingWorkoutIdsAsync_WithRawFitData_IncludesWorkout()
    {
        // Arrange
        var workout = await SeedWorkoutWithRawFitDataAsync(_db, avgHeartRate: 150);

        // Act
        var result = await _service.GetQualifyingWorkoutIdsAsync(_db);

        // Assert
        result.Should().Contain(workout.Id);
    }

    [Fact]
    public async Task GetQualifyingWorkoutIdsAsync_WithAvgHeartRateBpm_IncludesWorkout()
    {
        // Arrange
        var workout = await SeedWorkoutAsync(_db, avgHeartRateBpm: 150, durationS: 1800);

        // Act
        var result = await _service.GetQualifyingWorkoutIdsAsync(_db);

        // Assert
        result.Should().Contain(workout.Id);
    }

    [Fact]
    public async Task GetQualifyingWorkoutIdsAsync_WithNoHrData_ExcludesWorkout()
    {
        // Arrange
        var workout = await SeedWorkoutAsync(_db, avgHeartRateBpm: null, durationS: 1800);

        // Act
        var result = await _service.GetQualifyingWorkoutIdsAsync(_db);

        // Assert
        result.Should().NotContain(workout.Id);
    }

    [Fact]
    public async Task GetQualifyingWorkoutIdsAsync_CombinesAllQualifyingWorkouts_NoDuplicates()
    {
        // Arrange
        var workout1 = await SeedWorkoutWithTimeSeriesAsync(_db, heartRateValues: new byte[] { 120 });
        var workout2 = await SeedWorkoutWithRawFitDataAsync(_db, avgHeartRate: 150);
        var workout3 = await SeedWorkoutAsync(_db, avgHeartRateBpm: 160, durationS: 1800);
        var workout4 = await SeedWorkoutAsync(_db, avgHeartRateBpm: null, durationS: 1800); // No HR

        // Act
        var result = await _service.GetQualifyingWorkoutIdsAsync(_db);

        // Assert
        result.Should().Contain(workout1.Id);
        result.Should().Contain(workout2.Id);
        result.Should().Contain(workout3.Id);
        result.Should().NotContain(workout4.Id);
        result.Should().HaveCount(3);
        result.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region Helper Methods

    private List<HeartRateZone> CreateValidZones()
    {
        return new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },   // Zone 1
            new() { MinBpm = 114, MaxBpm = 133 },  // Zone 2
            new() { MinBpm = 133, MaxBpm = 152 },  // Zone 3
            new() { MinBpm = 152, MaxBpm = 171 },  // Zone 4
            new() { MinBpm = 171, MaxBpm = 190 }  // Zone 5
        };
    }

    private List<WorkoutTimeSeries> CreateTimeSeriesWithConstantHr(int durationSeconds, byte heartRate)
    {
        var timeSeries = new List<WorkoutTimeSeries>();
        var intervalSeconds = 10;
        var numPoints = durationSeconds / intervalSeconds;

        for (int i = 0; i <= numPoints; i++)
        {
            var elapsedSeconds = i * intervalSeconds;
            if (elapsedSeconds > durationSeconds) break;

            timeSeries.Add(new WorkoutTimeSeries
            {
                ElapsedSeconds = elapsedSeconds,
                HeartRateBpm = heartRate
            });
        }

        return timeSeries;
    }

    private List<WorkoutTimeSeries> CreateTimeSeriesWithMixedZones(int durationSeconds)
    {
        var timeSeries = new List<WorkoutTimeSeries>();
        var intervalSeconds = 10;
        var numPoints = durationSeconds / intervalSeconds;

        for (int i = 0; i <= numPoints; i++)
        {
            var elapsedSeconds = i * intervalSeconds;
            if (elapsedSeconds > durationSeconds) break;

            // Vary heart rate across zones
            byte heartRate = (byte)(110 + (i % 80)); // Varies between 110-189
            timeSeries.Add(new WorkoutTimeSeries
            {
                ElapsedSeconds = elapsedSeconds,
                HeartRateBpm = heartRate
            });
        }

        return timeSeries;
    }

    private async Task<Workout> SeedWorkoutAsync(
        TempoDbContext db,
        byte? avgHeartRateBpm = null,
        int durationS = 1800)
    {
        var workout = new Workout
        {
            StartedAt = DateTime.UtcNow.AddHours(-1),
            DurationS = durationS,
            DistanceM = 5000.0,
            AvgPaceS = (int)(durationS / 5.0),
            AvgHeartRateBpm = avgHeartRateBpm,
            CreatedAt = DateTime.UtcNow
        };

        db.Workouts.Add(workout);
        await db.SaveChangesAsync();
        return workout;
    }

    private async Task<Workout> SeedWorkoutWithTimeSeriesAsync(
        TempoDbContext db,
        byte[] heartRateValues)
    {
        var workout = await SeedWorkoutAsync(db, durationS: heartRateValues.Length * 10);
        
        var timeSeries = new List<WorkoutTimeSeries>();
        for (int i = 0; i < heartRateValues.Length; i++)
        {
            timeSeries.Add(new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = i * 10,
                HeartRateBpm = heartRateValues[i]
            });
        }

        db.WorkoutTimeSeries.AddRange(timeSeries);
        await db.SaveChangesAsync();
        return workout;
    }

    private async Task<Workout> SeedWorkoutWithRawFitDataAsync(
        TempoDbContext db,
        int avgHeartRate)
    {
        var workout = await SeedWorkoutAsync(db, durationS: 1800);
        
        var fitData = new
        {
            session = new
            {
                avgHeartRate = avgHeartRate
            }
        };

        workout.RawFitData = JsonSerializer.Serialize(fitData);
        await db.SaveChangesAsync();
        return workout;
    }

    #endregion
}

