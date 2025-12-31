using System.Text;
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
/// Unit tests for SplitRecalculationService
/// </summary>
public class SplitRecalculationServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly GpxParserService _gpxParser;
    private readonly FitParserService _fitParser;
    private readonly ILogger<SplitRecalculationService> _logger;
    private readonly SplitRecalculationService _service;

    public SplitRecalculationServiceTests()
    {
        // Create in-memory SQLite database for testing
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();

        var elevationConfig = new ElevationCalculationConfig
        {
            NoiseThresholdMeters = 2.0,
            MinDistanceMeters = 10.0
        };
        _gpxParser = new GpxParserService(elevationConfig);
        _fitParser = new FitParserService(elevationConfig);
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<SplitRecalculationService>();
        _service = new SplitRecalculationService(_db, _gpxParser, _fitParser, _logger);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task RecalculateSplitsForWorkoutAsync_WithMetricPreference_Returns1000mSplits()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: 1800);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        
        // Add RawGpxData with track points
        var trackPoints = CreateTestTrackPoints(5000.0, 1800);
        var rawGpxData = CreateRawGpxDataJson(trackPoints);
        workout.RawGpxData = rawGpxData;
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.RecalculateSplitsForWorkoutAsync(workout, "metric");

        // Assert
        result.Should().BeTrue();
        var splits = await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync();
        splits.Should().HaveCountGreaterThan(0);
        // First split should be approximately 1000m (metric)
        splits[0].DistanceM.Should().BeApproximately(1000.0, 100.0);
    }

    [Fact]
    public async Task RecalculateSplitsForWorkoutAsync_WithImperialPreference_Returns1609mSplits()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 8046.72, durationS: 1800); // ~5 miles
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        
        // Add RawGpxData with track points
        var trackPoints = CreateTestTrackPoints(8046.72, 1800);
        var rawGpxData = CreateRawGpxDataJson(trackPoints);
        workout.RawGpxData = rawGpxData;
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.RecalculateSplitsForWorkoutAsync(workout, "imperial");

        // Assert
        result.Should().BeTrue();
        var splits = await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync();
        splits.Should().HaveCountGreaterThan(0);
        // First split should be approximately 1609m (1 mile)
        splits[0].DistanceM.Should().BeApproximately(1609.344, 100.0);
    }

    [Fact]
    public async Task RecalculateSplitsForWorkoutAsync_WithoutRoute_ReturnsFalse()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);

        // Act
        var result = await _service.RecalculateSplitsForWorkoutAsync(workout, "metric");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RecalculateSplitsForWorkoutAsync_WithInsufficientTrackPoints_ReturnsFalse()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        // Don't add RawGpxData, and create a route with only 1 point (insufficient)
        var route = await _db.WorkoutRoutes.FirstOrDefaultAsync(r => r.WorkoutId == workout.Id);
        if (route != null)
        {
            route.RouteGeoJson = JsonSerializer.Serialize(new
            {
                type = "LineString",
                coordinates = new[] { new[] { 0.0, 0.0 } } // Only 1 point
            });
            await _db.SaveChangesAsync();
        }

        // Act
        var result = await _service.RecalculateSplitsForWorkoutAsync(workout, "metric");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RecalculateSplitsForWorkoutAsync_ExtractsFromRawGpxData_Correctly()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: 1800);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        
        var trackPoints = CreateTestTrackPoints(5000.0, 1800);
        var rawGpxData = CreateRawGpxDataJson(trackPoints);
        workout.RawGpxData = rawGpxData;
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.RecalculateSplitsForWorkoutAsync(workout, "metric");

        // Assert
        result.Should().BeTrue();
        var splits = await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync();
        splits.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task RecalculateSplitsForWorkoutAsync_ExtractsFromRawFileData_Correctly()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: 1800);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        
        // Create a GPX file with enough track points to generate splits
        var gpxXml = CreateGpxWithManyTrackPoints();
        workout.RawFileData = Encoding.UTF8.GetBytes(gpxXml);
        workout.RawFileType = "gpx";
        workout.RawFileName = "test.gpx";
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.RecalculateSplitsForWorkoutAsync(workout, "metric");

        // Assert
        result.Should().BeTrue();
        var splits = await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync();
        splits.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task RecalculateSplitsForWorkoutAsync_ExtractsFromRouteGeoJson_Correctly()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: 1800);
        
        // Create route with multiple coordinates (no RawGpxData or RawFileData)
        var coordinates = new List<double[]>();
        for (int i = 0; i < 100; i++)
        {
            coordinates.Add(new[] { 0.0 + (i * 0.0001), 0.0 + (i * 0.0001) });
        }
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout, coordinates);
        
        // Ensure no RawGpxData or RawFileData
        workout.RawGpxData = null;
        workout.RawFileData = null;
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.RecalculateSplitsForWorkoutAsync(workout, "metric");

        // Assert
        result.Should().BeTrue();
        var splits = await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync();
        splits.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task RecalculateSplitsForAllWorkoutsAsync_WithMultipleWorkouts_ReturnsCorrectCounts()
    {
        // Arrange
        var workout1 = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: 1800);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout1);
        var trackPoints1 = CreateTestTrackPoints(5000.0, 1800);
        workout1.RawGpxData = CreateRawGpxDataJson(trackPoints1);
        
        var workout2 = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 3000.0, durationS: 1200);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout2);
        var trackPoints2 = CreateTestTrackPoints(3000.0, 1200);
        workout2.RawGpxData = CreateRawGpxDataJson(trackPoints2);
        
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.RecalculateSplitsForAllWorkoutsAsync("metric");

        // Assert
        result.Should().NotBeNull();
        result.TotalWorkouts.Should().Be(2);
        result.SuccessCount.Should().Be(2);
        result.ErrorCount.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task RecalculateSplitsForAllWorkoutsAsync_WithErrors_LogsAndContinues()
    {
        // Arrange
        var workout1 = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: 1800);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout1);
        var trackPoints1 = CreateTestTrackPoints(5000.0, 1800);
        workout1.RawGpxData = CreateRawGpxDataJson(trackPoints1);
        
        // Workout2 with route but insufficient track points (will fail)
        var workout2 = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 3000.0, durationS: 1200);
        // Create route with only 1 point (insufficient for splits)
        var route2 = await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout2);
        route2.RouteGeoJson = JsonSerializer.Serialize(new
        {
            type = "LineString",
            coordinates = new[] { new[] { 0.0, 0.0 } } // Only 1 point - insufficient
        });
        _db.WorkoutRoutes.Update(route2);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.RecalculateSplitsForAllWorkoutsAsync("metric");

        // Assert
        result.Should().NotBeNull();
        result.TotalWorkouts.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.ErrorCount.Should().Be(1);
        result.Errors.Should().HaveCount(1);
    }

    [Fact]
    public async Task RecalculateSplitsForWorkoutAsync_DeletesExistingSplits_BeforeRecalculating()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000.0, durationS: 1800);
        await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithSplitsAsync(_db, workout, splitDistanceM: 500.0); // Old splits with 500m
        
        var trackPoints = CreateTestTrackPoints(5000.0, 1800);
        workout.RawGpxData = CreateRawGpxDataJson(trackPoints);
        await _db.SaveChangesAsync();

        var oldSplitsCount = await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).CountAsync();

        // Act
        var result = await _service.RecalculateSplitsForWorkoutAsync(workout, "metric");

        // Assert
        result.Should().BeTrue();
        var newSplits = await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync();
        newSplits.Should().HaveCountGreaterThan(0);
        // New splits should be approximately 1000m (not 500m)
        newSplits[0].DistanceM.Should().BeApproximately(1000.0, 100.0);
    }

    // Helper methods

    private List<GpxParserService.GpxPoint> CreateTestTrackPoints(double totalDistanceMeters, int totalDurationSeconds)
    {
        var numPoints = 100;
        var points = new List<GpxParserService.GpxPoint>();
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        
        // Create points along a line
        var startLat = 37.7749;
        var startLon = -122.4194;
        var degreeIncrement = totalDistanceMeters / (111000.0 * (numPoints - 1));

        for (int i = 0; i < numPoints; i++)
        {
            var elapsedSeconds = (int)((double)i / (numPoints - 1) * totalDurationSeconds);
            points.Add(new GpxParserService.GpxPoint
            {
                Latitude = startLat + (i * degreeIncrement),
                Longitude = startLon + (i * degreeIncrement),
                Time = startTime.AddSeconds(elapsedSeconds),
                Elevation = 100.0 + (i * 0.1)
            });
        }

        return points;
    }

    private string CreateRawGpxDataJson(List<GpxParserService.GpxPoint> trackPoints)
    {
        var rawGpxData = new
        {
            metadata = new Dictionary<string, object?>(),
            extensions = new Dictionary<string, object>(),
            trackPoints = trackPoints.Select(p => new
            {
                lat = p.Latitude,
                lon = p.Longitude,
                ele = p.Elevation,
                time = p.Time?.ToString("O"),
                hr = p.HeartRateBpm,
                cad = p.CadenceRpm,
                power = p.PowerWatts,
                temp = p.TemperatureC
            }).ToList(),
            calculated = new Dictionary<string, object>(),
            source = "gpx_import",
            importedAt = DateTime.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(rawGpxData);
    }

    private string CreateMinimalGpxXml()
    {
        return @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt lat=""37.7750"" lon=""-122.4195"">
        <time>2024-01-15T10:01:00Z</time>
      </trkpt>
      <trkpt lat=""37.7751"" lon=""-122.4196"">
        <time>2024-01-15T10:02:00Z</time>
      </trkpt>
      <trkpt lat=""37.7752"" lon=""-122.4197"">
        <time>2024-01-15T10:03:00Z</time>
      </trkpt>
      <trkpt lat=""37.7753"" lon=""-122.4198"">
        <time>2024-01-15T10:04:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
    }

    private string CreateGpxWithManyTrackPoints()
    {
        // Create GPX with many track points to ensure splits can be calculated
        // Need enough distance between points to generate splits (at least 5km total)
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">");
        sb.AppendLine(@"  <trk>");
        sb.AppendLine(@"    <trkseg>");
        
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var startLat = 37.7749;
        var startLon = -122.4194;
        
        // Create 100 track points with sufficient distance between them
        // For 5km total distance: 5000m / 111000m per degree / 99 segments ≈ 0.00045 degrees per point
        var degreeIncrement = 5000.0 / (111000.0 * 99);
        
        for (int i = 0; i < 100; i++)
        {
            var lat = startLat + (i * degreeIncrement);
            var lon = startLon + (i * degreeIncrement);
            var time = startTime.AddSeconds(i * 18); // 18 seconds per point = 30 minutes total
            sb.AppendLine($@"      <trkpt lat=""{lat:F6}"" lon=""{lon:F6}"">");
            sb.AppendLine($@"        <time>{time:yyyy-MM-ddTHH:mm:ss}Z</time>");
            sb.AppendLine(@"      </trkpt>");
        }
        
        sb.AppendLine(@"    </trkseg>");
        sb.AppendLine(@"  </trk>");
        sb.AppendLine(@"</gpx>");
        
        return sb.ToString();
    }
}

