using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for RouteMatchingService
/// </summary>
public class RouteMatchingServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly RouteMatchingService _service;
    private readonly ILogger<RouteMatchingService> _logger;

    public RouteMatchingServiceTests()
    {
        // Create in-memory SQLite database for testing
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();

        // Create logger
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<RouteMatchingService>();

        _service = new RouteMatchingService(_db, _logger);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    #region FindSimilarRoutesAsync Tests

    [Fact]
    public async Task FindSimilarRoutesAsync_ReturnsMatches_WhenRoutesAreSimilar()
    {
        // Arrange
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        // Create current workout with route
        var currentWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate,
            5000.0, // 5km
            1800, // 30 minutes
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749), // San Francisco start
                (-122.4094, 37.7849), // San Francisco end
            }));

        // Create similar workout (same route, different date)
        var similarWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate.AddDays(-30),
            5000.0,
            1750,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749), // Same start
                (-122.4094, 37.7849), // Same end
            }));

        // Act
        var result = await _service.FindSimilarRoutesAsync(currentWorkout.Id);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(m => m.WorkoutId == similarWorkout.Id);
    }

    [Fact]
    public async Task FindSimilarRoutesAsync_ReturnsEmpty_WhenNoSimilarRoutes()
    {
        // Arrange
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        var currentWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate,
            5000.0,
            1800,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749), // San Francisco
                (-122.4094, 37.7849),
            }));

        // Create workout with completely different route (New York)
        var differentWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate.AddDays(-30),
            5000.0,
            1750,
            CreateRouteGeoJson(new[] {
                (-74.0060, 40.7128), // New York
                (-73.9960, 40.7228),
            }));

        // Act
        var result = await _service.FindSimilarRoutesAsync(currentWorkout.Id);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindSimilarRoutesAsync_ReturnsEmpty_WhenWorkoutNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _service.FindSimilarRoutesAsync(nonExistentId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindSimilarRoutesAsync_ReturnsEmpty_WhenWorkoutHasNoRoute()
    {
        // Arrange
        var workout = new Workout
        {
            StartedAt = DateTime.UtcNow,
            DistanceM = 5000.0,
            DurationS = 1800,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };
        _db.Workouts.Add(workout);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.FindSimilarRoutesAsync(workout.Id);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindSimilarRoutesAsync_RespectsMaxResults()
    {
        // Arrange
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        var currentWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate,
            5000.0,
            1800,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Create 15 similar workouts
        for (int i = 1; i <= 15; i++)
        {
            await CreateWorkoutWithRouteAsync(
                _db,
                baseDate.AddDays(-i),
                5000.0,
                1750 + i,
                CreateRouteGeoJson(new[] {
                    (-122.4194, 37.7749),
                    (-122.4094, 37.7849),
                }));
        }

        // Act
        var result = await _service.FindSimilarRoutesAsync(currentWorkout.Id, maxResults: 10);

        // Assert
        result.Should().HaveCount(10);
    }

    [Fact]
    public async Task FindSimilarRoutesAsync_FiltersByTimeRange()
    {
        // Arrange
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        var currentWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate,
            5000.0,
            1800,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Create workout within 2 years
        var recentWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate.AddYears(-1),
            5000.0,
            1750,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Create workout outside 2 years (should be excluded)
        var oldWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate.AddYears(-3),
            5000.0,
            1750,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Act
        var result = await _service.FindSimilarRoutesAsync(currentWorkout.Id, maxYears: 2);

        // Assert
        result.Should().Contain(m => m.WorkoutId == recentWorkout.Id);
        result.Should().NotContain(m => m.WorkoutId == oldWorkout.Id);
    }

    [Fact]
    public async Task FindSimilarRoutesAsync_IncludesWorkoutsInSymmetricTimeWindow()
    {
        // Arrange
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        var currentWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate,
            5000.0,
            1800,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Create workout before current workout (should be included)
        var previousWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate.AddDays(-10),
            5000.0,
            1750,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Create workout after current workout (should now be included for symmetric matching)
        var futureWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate.AddDays(10),
            5000.0,
            1750,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Create workout outside maxYears window (should be excluded)
        var tooOldWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate.AddYears(-3), // Outside 2-year window
            5000.0,
            1750,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Act
        var result = await _service.FindSimilarRoutesAsync(currentWorkout.Id, maxYears: 2);

        // Assert
        result.Should().Contain(m => m.WorkoutId == previousWorkout.Id);
        result.Should().Contain(m => m.WorkoutId == futureWorkout.Id); // Now included for symmetric matching
        result.Should().NotContain(m => m.WorkoutId == tooOldWorkout.Id); // Still excluded if outside window
    }

    [Fact]
    public async Task FindSimilarRoutesAsync_HandlesRoutesWithFewPoints()
    {
        // Arrange
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        // Create workout with only 1 point (invalid)
        var workout = new Workout
        {
            StartedAt = baseDate,
            DistanceM = 5000.0,
            DurationS = 1800,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };
        _db.Workouts.Add(workout);
        
        var route = new WorkoutRoute
        {
            WorkoutId = workout.Id,
            RouteGeoJson = CreateRouteGeoJson(new[] { (-122.4194, 37.7749) }) // Only 1 point
        };
        _db.WorkoutRoutes.Add(route);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.FindSimilarRoutesAsync(workout.Id);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindSimilarRoutesAsync_FiltersByStartEndProximity()
    {
        // Arrange
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        var currentWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate,
            5000.0,
            1800,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749), // Start
                (-122.4094, 37.7849), // End
            }));

        // Create workout with start point too far (> 100m)
        var farStartWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate.AddDays(-30),
            5000.0,
            1750,
            CreateRouteGeoJson(new[] {
                (-122.4294, 37.7649), // Start far away (> 100m)
                (-122.4094, 37.7849), // End close
            }));

        // Act
        var result = await _service.FindSimilarRoutesAsync(currentWorkout.Id);

        // Assert
        result.Should().NotContain(m => m.WorkoutId == farStartWorkout.Id);
    }

    [Fact]
    public async Task FindSimilarRoutesAsync_FiltersByDistanceSimilarity()
    {
        // Arrange
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        var currentWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate,
            5000.0, // 5km
            1800,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Create workout with distance too different (> 10% = 500m difference)
        var differentDistanceWorkout = await CreateWorkoutWithRouteAsync(
            _db,
            baseDate.AddDays(-30),
            6000.0, // 6km (20% difference, > 10% threshold)
            1750,
            CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            }));

        // Act
        var result = await _service.FindSimilarRoutesAsync(currentWorkout.Id);

        // Assert
        result.Should().NotContain(m => m.WorkoutId == differentDistanceWorkout.Id);
    }

    #endregion

    #region CalculateRouteSimilarity Tests

    [Fact]
    public void CalculateRouteSimilarity_ReturnsHighScore_WhenRoutesMatch()
    {
        // Arrange
        var route1 = new WorkoutRoute
        {
            RouteGeoJson = CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            })
        };

        var route2 = new WorkoutRoute
        {
            RouteGeoJson = CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            })
        };

        // Act
        var result = _service.CalculateRouteSimilarity(route1, route2);

        // Assert
        result.Should().BeGreaterThan(50.0); // High similarity
    }

    [Fact]
    public void CalculateRouteSimilarity_ReturnsLowScore_WhenRoutesDiffer()
    {
        // Arrange
        var route1 = new WorkoutRoute
        {
            RouteGeoJson = CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749), // San Francisco
                (-122.4094, 37.7849),
            })
        };

        var route2 = new WorkoutRoute
        {
            RouteGeoJson = CreateRouteGeoJson(new[] {
                (-74.0060, 40.7128), // New York
                (-73.9960, 40.7228),
            })
        };

        // Act
        var result = _service.CalculateRouteSimilarity(route1, route2);

        // Assert
        result.Should().BeLessThan(50.0); // Low similarity
    }

    #endregion

    #region AreRoutesSimilar Tests

    [Fact]
    public void AreRoutesSimilar_ReturnsTrue_WhenAverageDistanceBelowThreshold()
    {
        // Arrange
        var route1 = new WorkoutRoute
        {
            RouteGeoJson = CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            })
        };

        var route2 = new WorkoutRoute
        {
            RouteGeoJson = CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749),
                (-122.4094, 37.7849),
            })
        };

        // Act
        var result = _service.AreRoutesSimilar(route1, route2, threshold: 50.0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void AreRoutesSimilar_ReturnsFalse_WhenAverageDistanceAboveThreshold()
    {
        // Arrange
        var route1 = new WorkoutRoute
        {
            RouteGeoJson = CreateRouteGeoJson(new[] {
                (-122.4194, 37.7749), // San Francisco
                (-122.4094, 37.7849),
            })
        };

        var route2 = new WorkoutRoute
        {
            RouteGeoJson = CreateRouteGeoJson(new[] {
                (-74.0060, 40.7128), // New York
                (-73.9960, 40.7228),
            })
        };

        // Act
        var result = _service.AreRoutesSimilar(route1, route2, threshold: 50.0);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ExtractCoordinatesFromGeoJson Tests

    [Fact]
    public void ExtractCoordinatesFromGeoJson_ReturnsCoordinates_WhenValidGeoJson()
    {
        // Arrange
        var geoJson = CreateRouteGeoJson(new[] {
            (-122.4194, 37.7749),
            (-122.4094, 37.7849),
            (-122.3994, 37.7949),
        });

        // Act
        var result = _service.ExtractCoordinatesFromGeoJson(geoJson);

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().Be((37.7749, -122.4194)); // lat, lon
        result[1].Should().Be((37.7849, -122.4094));
        result[2].Should().Be((37.7949, -122.3994));
    }

    [Fact]
    public void ExtractCoordinatesFromGeoJson_ReturnsEmpty_WhenInvalidGeoJson()
    {
        // Arrange
        var invalidJson = "{ invalid json }";

        // Act
        var result = _service.ExtractCoordinatesFromGeoJson(invalidJson);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractCoordinatesFromGeoJson_ReturnsEmpty_WhenEmptyRoute()
    {
        // Arrange
        var emptyGeoJson = JsonSerializer.Serialize(new
        {
            type = "LineString",
            coordinates = Array.Empty<object>()
        });

        // Act
        var result = _service.ExtractCoordinatesFromGeoJson(emptyGeoJson);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractCoordinatesFromGeoJson_ReturnsEmpty_WhenMissingCoordinates()
    {
        // Arrange
        var geoJson = JsonSerializer.Serialize(new
        {
            type = "LineString"
            // Missing coordinates property
        });

        // Act
        var result = _service.ExtractCoordinatesFromGeoJson(geoJson);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractCoordinatesFromGeoJson_HandlesNullJson()
    {
        // Arrange
        var nullJson = "null";

        // Act
        var result = _service.ExtractCoordinatesFromGeoJson(nullJson);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region CalculateAveragePointDistance Tests

    [Fact]
    public void CalculateAveragePointDistance_ReturnsCorrectDistance()
    {
        // Arrange
        // Two routes that are very close (should have low average distance)
        var route1 = new List<(double lat, double lon)>
        {
            (37.7749, -122.4194),
            (37.7849, -122.4094),
        };

        var route2 = new List<(double lat, double lon)>
        {
            (37.7750, -122.4195), // Very close to route1
            (37.7850, -122.4095),
        };

        // Act
        var result = _service.CalculateAveragePointDistance(route1, route2);

        // Assert
        result.Should().BeLessThan(200.0); // Should be close (within 200m)
    }

    [Fact]
    public void CalculateAveragePointDistance_ReturnsMaxValue_WhenRouteHasFewPoints()
    {
        // Arrange
        var route1 = new List<(double lat, double lon)>
        {
            (37.7749, -122.4194),
        };

        var route2 = new List<(double lat, double lon)>
        {
            (37.7750, -122.4195),
            (37.7850, -122.4095),
        };

        // Act
        var result = _service.CalculateAveragePointDistance(route1, route2);

        // Assert
        result.Should().Be(double.MaxValue);
    }

    #endregion

    #region Helper Methods

    private async Task<Workout> CreateWorkoutWithRouteAsync(
        TempoDbContext db,
        DateTime startedAt,
        double distanceM,
        int durationS,
        string routeGeoJson)
    {
        var workout = new Workout
        {
            StartedAt = startedAt,
            DistanceM = distanceM,
            DurationS = durationS,
            AvgPaceS = (int)(durationS / (distanceM / 1000.0)),
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };
        db.Workouts.Add(workout);
        await db.SaveChangesAsync();

        var route = new WorkoutRoute
        {
            WorkoutId = workout.Id,
            RouteGeoJson = routeGeoJson
        };
        db.WorkoutRoutes.Add(route);
        await db.SaveChangesAsync();

        return workout;
    }

    private string CreateRouteGeoJson((double lon, double lat)[] coordinates)
    {
        var coords = coordinates.Select(c => new[] { c.lon, c.lat }).ToArray();
        return JsonSerializer.Serialize(new
        {
            type = "LineString",
            coordinates = coords
        });
    }

    #endregion
}

