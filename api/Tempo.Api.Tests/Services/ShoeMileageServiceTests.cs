using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for ShoeMileageService
/// </summary>
public class ShoeMileageServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly ShoeMileageService _service;
    private readonly ILogger<ShoeMileageService> _logger;
    private readonly SqliteConnection _connection;

    public ShoeMileageServiceTests()
    {
        // Create in-memory SQLite database
        // Keep connection open for shared in-memory database to work properly
        _connection = new SqliteConnection("Data Source=:memory:?cache=shared");
        _connection.Open();
        
        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();

        // Create logger mock
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ShoeMileageService>();

        _service = new ShoeMileageService();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithNoWorkouts_ReturnsInitialMileageOnly()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: 1000.0);

        // Act
        var result = await _service.GetTotalMileageAsync(_db, shoe.Id, "metric");

        // Assert
        result.Should().BeApproximately(1.0, 0.001); // 1000m = 1km
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithNoWorkoutsAndNoInitialMileage_ReturnsZero()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: null);

        // Act
        var result = await _service.GetTotalMileageAsync(_db, shoe.Id, "metric");

        // Assert
        result.Should().Be(0.0);
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithWorkouts_AggregatesDistancesCorrectly()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: null);
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 5000.0); // 5km
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 3000.0); // 3km
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 2000.0); // 2km

        // Act
        var result = await _service.GetTotalMileageAsync(_db, shoe.Id, "metric");

        // Assert
        result.Should().BeApproximately(10.0, 0.001); // 10km total
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithInitialMileage_AddsToWorkoutDistances()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: 2000.0); // 2km initial
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 5000.0); // 5km workout

        // Act
        var result = await _service.GetTotalMileageAsync(_db, shoe.Id, "metric");

        // Assert
        result.Should().BeApproximately(7.0, 0.001); // 2km + 5km = 7km
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithMetricUnits_ReturnsKilometers()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: null);
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 5000.0); // 5000m
        
        // Verify the workout was saved with correct distance
        var savedWorkout = await _db.Workouts.FindAsync(workout.Id);
        savedWorkout.Should().NotBeNull();
        savedWorkout!.DistanceM.Should().Be(5000.0);
        savedWorkout.ShoeId.Should().Be(shoe.Id);

        // Act
        var result = await _service.GetTotalMileageAsync(_db, shoe.Id, "metric");

        // Assert
        result.Should().BeApproximately(5.0, 0.001); // 5000m / 1000 = 5km
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithImperialUnits_ReturnsMiles()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: null);
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 1609.344); // 1 mile in meters

        // Act
        var result = await _service.GetTotalMileageAsync(_db, shoe.Id, "imperial");

        // Assert
        result.Should().BeApproximately(1.0, 0.001); // 1609.344m / 1609.344 = 1 mile
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithNonExistentShoe_ReturnsZero()
    {
        // Arrange
        var nonExistentShoeId = Guid.NewGuid();

        // Act
        var result = await _service.GetTotalMileageAsync(_db, nonExistentShoeId, "metric");

        // Assert
        result.Should().Be(0.0);
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithZeroInitialMileage_OnlyCountsWorkouts()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: 0.0);
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 5000.0);

        // Act
        var result = await _service.GetTotalMileageAsync(_db, shoe.Id, "metric");

        // Assert
        result.Should().BeApproximately(5.0, 0.001); // Only workout distance
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithMultipleWorkoutsAndInitialMileage_CalculatesCorrectly()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: 10000.0); // 10km initial
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 5000.0); // 5km
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 3000.0); // 3km
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 2000.0); // 2km

        // Act
        var result = await _service.GetTotalMileageAsync(_db, shoe.Id, "metric");

        // Assert
        result.Should().BeApproximately(20.0, 0.001); // 10km + 5km + 3km + 2km = 20km
    }

    [Fact]
    public async Task GetTotalMileageWithUserPreferenceAsync_UsesSettingsUnitPreference()
    {
        // Arrange - clear any existing settings first
        _db.UserSettings.RemoveRange(_db.UserSettings);
        await _db.SaveChangesAsync();
        
        await TestDataSeeder.SeedUserSettingsAsync(_db, unitPreference: "imperial");
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: null);
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 1609.344); // 1 mile

        // Act
        var result = await _service.GetTotalMileageWithUserPreferenceAsync(_db, shoe.Id);

        // Assert
        result.Should().BeApproximately(1.0, 0.001); // Should return in miles
    }

    [Fact]
    public async Task GetTotalMileageWithUserPreferenceAsync_WithNoSettings_DefaultsToMetric()
    {
        // Arrange
        // No settings seeded
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: null);
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 5000.0); // 5km

        // Act
        var result = await _service.GetTotalMileageWithUserPreferenceAsync(_db, shoe.Id);

        // Assert
        result.Should().BeApproximately(5.0, 0.001); // Should default to metric (km)
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithWorkoutsWithZeroDistance_StillCounts()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: null);
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 5000.0);
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 0.0); // Zero distance workout

        // Act
        var result = await _service.GetTotalMileageAsync(_db, shoe.Id, "metric");

        // Assert
        result.Should().BeApproximately(5.0, 0.001); // Only non-zero distance counts
    }

    [Fact]
    public async Task GetTotalMileageAsync_WithCaseInsensitiveUnitPreference_WorksCorrectly()
    {
        // Arrange
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Pegasus", initialMileage: null);
        await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id, distanceM: 5000.0);

        // Act
        var resultMetric = await _service.GetTotalMileageAsync(_db, shoe.Id, "METRIC");
        var resultImperial = await _service.GetTotalMileageAsync(_db, shoe.Id, "IMPERIAL");

        // Assert
        resultMetric.Should().BeApproximately(5.0, 0.001); // 5km
        resultImperial.Should().BeApproximately(3.10686, 0.01); // ~3.1 miles
    }
}

