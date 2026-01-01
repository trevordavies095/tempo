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
/// Unit tests for BestEffortService covering calculation logic and error handling
/// </summary>
public class BestEffortServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly BestEffortService _service;
    private readonly ILogger<BestEffortService> _logger;
    private readonly SqliteConnection _connection;

    public BestEffortServiceTests()
    {
        // Create in-memory SQLite database
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        
        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();
        
        // Enable foreign key constraints for SQLite
        _db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        // Create logger
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<BestEffortService>();

        _service = new BestEffortService(_logger);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    #region GetBestEffortsAsync Tests

    [Fact]
    public async Task GetBestEffortsAsync_WithNoBestEfforts_ReturnsEmptyList()
    {
        // Act
        var result = await _service.GetBestEffortsAsync(_db);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBestEffortsAsync_WithBestEfforts_ReturnsOrderedList()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 10000);
        
        var bestEffort1 = new BestEffort
        {
            Distance = "5K",
            DistanceM = 5000,
            TimeS = 1200,
            WorkoutId = workout.Id,
            WorkoutDate = workout.StartedAt,
            CalculatedAt = DateTime.UtcNow
        };
        
        var bestEffort2 = new BestEffort
        {
            Distance = "10K",
            DistanceM = 10000,
            TimeS = 2400,
            WorkoutId = workout.Id,
            WorkoutDate = workout.StartedAt,
            CalculatedAt = DateTime.UtcNow
        };
        
        var bestEffort3 = new BestEffort
        {
            Distance = "1K",
            DistanceM = 1000,
            TimeS = 240,
            WorkoutId = workout.Id,
            WorkoutDate = workout.StartedAt,
            CalculatedAt = DateTime.UtcNow
        };

        _db.BestEfforts.AddRange(bestEffort1, bestEffort2, bestEffort3);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetBestEffortsAsync(_db);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result[0].DistanceM.Should().Be(1000); // 1K
        result[1].DistanceM.Should().Be(5000); // 5K
        result[2].DistanceM.Should().Be(10000); // 10K
    }

    #endregion

    #region CalculateBestEffortForWorkoutAsync Tests

    [Fact]
    public async Task CalculateBestEffortForWorkoutAsync_WithWorkoutTooShort_ReturnsNull()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 1000); // 1K workout
        var targetDistance = 5000; // 5K target

        // Act
        var result = await _service.CalculateBestEffortForWorkoutAsync(_db, workout, "5K", targetDistance);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CalculateBestEffortForWorkoutAsync_WithTimeSeriesData_CalculatesCorrectly()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 10000, // 10K workout
            durationS: 2400, // 40 minutes
            includeTimeSeries: true);

        var targetDistance = 5000; // 5K target

        // Act
        var result = await _service.CalculateBestEffortForWorkoutAsync(_db, workout, "5K", targetDistance);

        // Assert
        result.Should().NotBeNull();
        result!.Distance.Should().Be("5K");
        result.DistanceM.Should().Be(5000);
        result.TimeS.Should().BeGreaterThan(0);
        result.WorkoutId.Should().Be(workout.Id.ToString());
    }


    [Fact]
    public async Task CalculateBestEffortForWorkoutAsync_WithNoTimeSeriesAndNoRoute_ReturnsNull()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 10000);

        var targetDistance = 5000; // 5K target

        // Act
        var result = await _service.CalculateBestEffortForWorkoutAsync(_db, workout, "5K", targetDistance);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CalculateAllBestEffortsAsync Tests

    [Fact]
    public async Task CalculateAllBestEffortsAsync_WithNoWorkouts_ReturnsEmptyList()
    {
        // Act
        var result = await _service.CalculateAllBestEffortsAsync(_db);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        
        // Verify database is empty
        var dbBestEfforts = await _db.BestEfforts.ToListAsync();
        dbBestEfforts.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateAllBestEffortsAsync_WithWorkouts_CalculatesAndSavesBestEfforts()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 10000, // 10K workout
            durationS: 2400,
            includeTimeSeries: true);

        // Act
        var result = await _service.CalculateAllBestEffortsAsync(_db);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        
        // Should have best efforts for distances <= 10K
        var distances = result.Select(r => r.DistanceM).ToList();
        distances.Should().OnlyContain(d => d <= 10000);
        
        // Verify saved to database
        var dbBestEfforts = await _db.BestEfforts.ToListAsync();
        dbBestEfforts.Should().HaveCount(result.Count);
    }

    [Fact]
    public async Task CalculateAllBestEffortsAsync_ClearsExistingBestEfforts()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(_db, distanceM: 10000, includeTimeSeries: true);
        
        // Create existing best effort
        var existingBestEffort = new BestEffort
        {
            Distance = "5K",
            DistanceM = 5000,
            TimeS = 2000,
            WorkoutId = workout.Id,
            WorkoutDate = workout.StartedAt,
            CalculatedAt = DateTime.UtcNow
        };
        _db.BestEfforts.Add(existingBestEffort);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.CalculateAllBestEffortsAsync(_db);

        // Assert
        // Should have recalculated, so old best effort should be replaced
        var dbBestEfforts = await _db.BestEfforts.ToListAsync();
        dbBestEfforts.Should().NotContain(be => be.TimeS == 2000);
    }

    [Fact]
    public async Task CalculateAllBestEffortsAsync_WithMultipleWorkouts_SelectsFastest()
    {
        // Arrange
        // Create two workouts with different times for same distance
        var workout1 = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1500, // 25 minutes (slower)
            includeTimeSeries: true);
        
        var workout2 = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200, // 20 minutes (faster)
            includeTimeSeries: true);

        // Act
        var result = await _service.CalculateAllBestEffortsAsync(_db);

        // Assert
        var fiveKResult = result.FirstOrDefault(r => r.Distance == "5K");
        fiveKResult.Should().NotBeNull();
        fiveKResult!.WorkoutId.Should().Be(workout2.Id.ToString()); // Should use faster workout
    }

    #endregion

    #region UpdateBestEffortsForNewWorkoutAsync Tests

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_WithNewFasterTime_UpdatesBestEffort()
    {
        // Arrange
        var existingWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1500,
            includeTimeSeries: true);
        
        // Calculate initial best efforts
        await _service.CalculateAllBestEffortsAsync(_db);
        
        // Create new faster workout
        var newWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200, // Faster
            includeTimeSeries: true);

        // Act
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, newWorkout);

        // Assert
        var bestEffort = await _db.BestEfforts.FirstOrDefaultAsync(be => be.Distance == "5K");
        bestEffort.Should().NotBeNull();
        bestEffort!.WorkoutId.Should().Be(newWorkout.Id);
        bestEffort.TimeS.Should().BeLessThan(1500);
    }

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_WithSlowerTime_PreservesExisting()
    {
        // Arrange
        var existingWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200, // Fast
            includeTimeSeries: true);
        
        // Calculate initial best efforts
        await _service.CalculateAllBestEffortsAsync(_db);
        
        var existingBestEffort = await _db.BestEfforts.FirstOrDefaultAsync(be => be.Distance == "5K");
        var originalTimeS = existingBestEffort!.TimeS;
        
        // Create new slower workout
        var newWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1500, // Slower
            includeTimeSeries: true);

        // Act
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, newWorkout);

        // Assert
        var bestEffort = await _db.BestEfforts.FirstOrDefaultAsync(be => be.Distance == "5K");
        bestEffort.Should().NotBeNull();
        bestEffort!.TimeS.Should().Be(originalTimeS); // Should preserve faster time
        bestEffort.WorkoutId.Should().Be(existingWorkout.Id);
    }

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_WithWorkoutTooShort_SkipsDistances()
    {
        // Arrange
        var shortWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 1000, // Only 1K
            includeTimeSeries: true);

        // Act
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, shortWorkout);

        // Assert
        // Should not create best efforts for distances > 1K
        var longDistanceBestEfforts = await _db.BestEfforts
            .Where(be => be.DistanceM > 1000)
            .ToListAsync();
        longDistanceBestEfforts.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_WithNonExistentWorkout_LogsWarning()
    {
        // Arrange
        var nonExistentWorkout = new Workout
        {
            Id = Guid.NewGuid(),
            DistanceM = 5000,
            DurationS = 1200,
            StartedAt = DateTime.UtcNow
        };

        // Act
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, nonExistentWorkout);

        // Assert
        // Should not throw, but should log warning
        var bestEfforts = await _db.BestEfforts.ToListAsync();
        bestEfforts.Should().BeEmpty();
    }

    #endregion

    #region ClearBestEffortsAsync Tests

    [Fact]
    public async Task ClearBestEffortsAsync_WithBestEfforts_RemovesAll()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        
        var bestEffort1 = new BestEffort
        {
            Distance = "5K",
            DistanceM = 5000,
            TimeS = 1200,
            WorkoutId = workout.Id,
            WorkoutDate = workout.StartedAt,
            CalculatedAt = DateTime.UtcNow
        };
        
        var bestEffort2 = new BestEffort
        {
            Distance = "10K",
            DistanceM = 10000,
            TimeS = 2400,
            WorkoutId = workout.Id,
            WorkoutDate = workout.StartedAt,
            CalculatedAt = DateTime.UtcNow
        };

        _db.BestEfforts.AddRange(bestEffort1, bestEffort2);
        await _db.SaveChangesAsync();

        // Act
        await _service.ClearBestEffortsAsync(_db);

        // Assert
        var remaining = await _db.BestEfforts.ToListAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearBestEffortsAsync_WithNoBestEfforts_DoesNotThrow()
    {
        // Act
        await _service.ClearBestEffortsAsync(_db);

        // Assert
        var remaining = await _db.BestEfforts.ToListAsync();
        remaining.Should().BeEmpty();
    }

    #endregion

    #region StandardDistances Tests

    [Fact]
    public void StandardDistances_ContainsExpectedDistances()
    {
        // Act & Assert
        BestEffortService.StandardDistances.Should().ContainKey("400m");
        BestEffortService.StandardDistances.Should().ContainKey("1K");
        BestEffortService.StandardDistances.Should().ContainKey("5K");
        BestEffortService.StandardDistances.Should().ContainKey("10K");
        BestEffortService.StandardDistances.Should().ContainKey("Marathon");
    }

    [Fact]
    public void StandardDistances_HasCorrectValues()
    {
        // Act & Assert
        BestEffortService.StandardDistances["400m"].Should().Be(400);
        BestEffortService.StandardDistances["1K"].Should().Be(1000);
        BestEffortService.StandardDistances["5K"].Should().Be(5000);
        BestEffortService.StandardDistances["10K"].Should().Be(10000);
        BestEffortService.StandardDistances["Marathon"].Should().Be(42195);
    }

    #endregion
}

