using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for ExportService export format validation
/// </summary>
[Collection("Integration Tests")]
public class ExportServiceTests : IClassFixture<TempoWebApplicationFactory>, IDisposable
{
    private readonly TempoWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly TempoDbContext _db;
    private readonly ExportService _exportService;
    private readonly string _tempMediaDirectory;
    private readonly User _testUser;

    public ExportServiceTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
        
        // Create temporary media directory
        _tempMediaDirectory = Path.Combine(Path.GetTempPath(), $"tempo-test-media-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempMediaDirectory);

        // Get services from factory - store scope as field to prevent disposal
        _scope = factory.Server.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        
        var mediaConfig = new MediaStorageConfig 
        { 
            RootPath = _tempMediaDirectory, 
            MaxFileSizeBytes = 52_428_800 
        };
        
        var httpContextAccessor = _scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var logger = _scope.ServiceProvider.GetRequiredService<ILogger<ExportService>>();
        
        _exportService = new ExportService(_db, mediaConfig, httpContextAccessor, logger);
        
        // Ensure user exists for authentication (check first to avoid unique constraint violation)
        _testUser = _db.Users.FirstOrDefault(u => u.Username == "testuser");
        if (_testUser == null)
        {
            _testUser = TestDataSeeder.SeedUserAsync(_db).GetAwaiter().GetResult();
        }
        
        // Set up HTTP context with authenticated user
        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, _testUser.Id.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, _testUser.Username)
            }, "Test"));
        httpContextAccessor.HttpContext = httpContext;
    }

    public void Dispose()
    {
        // Dispose the service scope to clean up scoped services
        _scope?.Dispose();
        
        // Clean up temporary directory
        if (Directory.Exists(_tempMediaDirectory))
        {
            try
            {
                Directory.Delete(_tempMediaDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task ExportAllDataAsync_ProducesValidManifest()
    {
        // Arrange
        await SeedTestDataAsync();

        // Act
        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        // Assert
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        manifestEntry.Should().NotBeNull();

        using var manifestStream = manifestEntry!.Open();
        var manifestJson = await new StreamReader(manifestStream).ReadToEndAsync();
        var manifest = JsonSerializer.Deserialize<JsonElement>(manifestJson);
        
        manifest.GetProperty("version").GetString().Should().Be("1.0.0");
        manifest.TryGetProperty("tempoVersion", out _).Should().BeTrue();
        manifest.TryGetProperty("exportDate", out _).Should().BeTrue();
        manifest.TryGetProperty("exportedBy", out _).Should().BeTrue();
        manifest.TryGetProperty("statistics", out _).Should().BeTrue();
        manifest.TryGetProperty("dataFormat", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAllDataAsync_ProducesValidDataDirectory()
    {
        // Arrange
        await SeedTestDataAsync();

        // Act
        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        // Assert
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        
        // Verify all expected JSON files exist
        var expectedFiles = new[]
        {
            "data/settings.json",
            "data/shoes.json",
            "data/workouts.json",
            "data/routes.json",
            "data/splits.json",
            "data/time-series.json",
            "data/media-metadata.json",
            "data/best-efforts.json"
        };

        foreach (var file in expectedFiles)
        {
            var entry = archive.GetEntry(file);
            entry.Should().NotBeNull($"Expected file {file} to exist in export");
        }
    }

    [Fact]
    public async Task ExportAllDataAsync_ProducesValidWorkoutsDirectory()
    {
        // Arrange
        var workout = await SeedTestDataAsync();
        
        // Add raw file data to workout
        workout.RawFileData = new byte[] { 1, 2, 3, 4, 5 };
        workout.RawFileName = "test.gpx";
        workout.RawFileType = "gpx";
        await _db.SaveChangesAsync();

        // Act
        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        // Assert
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        
        // Verify workouts directory structure
        var rawFileEntry = archive.GetEntry($"workouts/{workout.Id}/raw/test.gpx");
        rawFileEntry.Should().NotBeNull("Raw file should be exported");
    }

    [Fact]
    public async Task ExportAllDataAsync_ManifestContainsCorrectStatistics()
    {
        // Arrange - Clean database first to ensure accurate counts
        await CleanDatabaseAsync();
        
        var shoe = await TestDataSeeder.SeedShoeAsync(_db);
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id);
        await TestDataSeeder.SeedWorkoutWithSplitsAsync(_db, workout);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout);
        await TestDataSeeder.SeedUserSettingsAsync(_db, defaultShoeId: shoe.Id);

        // Act
        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        // Assert
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        using var manifestStream = manifestEntry!.Open();
        var manifestJson = await new StreamReader(manifestStream).ReadToEndAsync();
        var manifest = JsonSerializer.Deserialize<JsonElement>(manifestJson);
        
        var statistics = manifest.GetProperty("statistics");
        statistics.GetProperty("settings").GetInt32().Should().Be(1);
        statistics.GetProperty("shoes").GetInt32().Should().Be(1);
        statistics.GetProperty("workouts").GetInt32().Should().Be(1);
        statistics.GetProperty("routes").GetInt32().Should().Be(0); // No route seeded
        statistics.GetProperty("splits").GetInt32().Should().BeGreaterThan(0);
        statistics.GetProperty("timeSeries").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportAllDataAsync_ManifestVersionIsCorrect()
    {
        // Arrange
        await SeedTestDataAsync();

        // Act
        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        // Assert
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        using var manifestStream = manifestEntry!.Open();
        var manifestJson = await new StreamReader(manifestStream).ReadToEndAsync();
        var manifest = JsonSerializer.Deserialize<JsonElement>(manifestJson);
        
        manifest.GetProperty("version").GetString().Should().Be("1.0.0");
    }

    [Fact]
    public async Task ExportAllDataAsync_ManifestContainsAllRequiredFields()
    {
        // Arrange
        await SeedTestDataAsync();

        // Act
        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        // Assert
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        using var manifestStream = manifestEntry!.Open();
        var manifestJson = await new StreamReader(manifestStream).ReadToEndAsync();
        var manifest = JsonSerializer.Deserialize<JsonElement>(manifestJson);
        
        // Verify all required fields
        manifest.TryGetProperty("version", out _).Should().BeTrue();
        manifest.TryGetProperty("tempoVersion", out _).Should().BeTrue();
        manifest.TryGetProperty("exportDate", out _).Should().BeTrue();
        manifest.TryGetProperty("exportedBy", out _).Should().BeTrue();
        manifest.TryGetProperty("statistics", out _).Should().BeTrue();
        manifest.TryGetProperty("dataFormat", out _).Should().BeTrue();
        
        // Verify dataFormat fields
        var dataFormat = manifest.GetProperty("dataFormat");
        dataFormat.TryGetProperty("shoes", out _).Should().BeTrue();
        dataFormat.TryGetProperty("workouts", out _).Should().BeTrue();
        dataFormat.TryGetProperty("routes", out _).Should().BeTrue();
        dataFormat.TryGetProperty("splits", out _).Should().BeTrue();
        dataFormat.TryGetProperty("timeSeries", out _).Should().BeTrue();
        dataFormat.TryGetProperty("mediaMetadata", out _).Should().BeTrue();
        dataFormat.TryGetProperty("bestEfforts", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAllDataAsync_JsonFilesAreValidAndDeserializable()
    {
        // Arrange
        await SeedTestDataAsync();

        // Act
        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        // Assert
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        
        var jsonFiles = new[]
        {
            "data/settings.json",
            "data/shoes.json",
            "data/workouts.json",
            "data/routes.json",
            "data/splits.json",
            "data/time-series.json",
            "data/media-metadata.json",
            "data/best-efforts.json"
        };

        foreach (var file in jsonFiles)
        {
            var entry = archive.GetEntry(file);
            if (entry != null)
            {
                using var stream = entry.Open();
                var json = await new StreamReader(stream).ReadToEndAsync();
                
                // Verify JSON is valid and deserializable
                var act = () => JsonSerializer.Deserialize<JsonElement>(json);
                act.Should().NotThrow($"File {file} should contain valid JSON");
            }
        }
    }

    [Fact]
    public async Task ExportAllDataAsync_ExportFromSeededDb_ProducesValidZip()
    {
        // Arrange - Clean database first to ensure accurate counts
        await CleanDatabaseAsync();
        
        // Seed comprehensive test data
        var shoe = await TestDataSeeder.SeedShoeAsync(_db);
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(_db, shoeId: shoe.Id);
        await TestDataSeeder.SeedUserSettingsAsync(_db, defaultShoeId: shoe.Id);
        
        // Create a best effort
        var bestEffort = new BestEffort
        {
            WorkoutId = workout.Id,
            Distance = "1km",
            DistanceM = 1000.0,
            TimeS = 240,
            WorkoutDate = workout.StartedAt,
            CalculatedAt = DateTime.UtcNow
        };
        _db.BestEfforts.Add(bestEffort);
        await _db.SaveChangesAsync();

        // Act
        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        // Assert - Verify ZIP structure
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        
        // Verify manifest exists
        var manifestEntry = archive.GetEntry("manifest.json");
        manifestEntry.Should().NotBeNull();
        
        // Verify all data files exist
        archive.GetEntry("data/settings.json").Should().NotBeNull();
        archive.GetEntry("data/shoes.json").Should().NotBeNull();
        archive.GetEntry("data/workouts.json").Should().NotBeNull();
        archive.GetEntry("data/routes.json").Should().NotBeNull();
        archive.GetEntry("data/splits.json").Should().NotBeNull();
        archive.GetEntry("data/time-series.json").Should().NotBeNull();
        archive.GetEntry("data/media-metadata.json").Should().NotBeNull();
        archive.GetEntry("data/best-efforts.json").Should().NotBeNull();
        
        // Verify README exists
        archive.GetEntry("README.txt").Should().NotBeNull();
        
        // Verify manifest can be deserialized
        using var manifestStream = manifestEntry!.Open();
        var manifestJson = await new StreamReader(manifestStream).ReadToEndAsync();
        var manifest = JsonSerializer.Deserialize<JsonElement>(manifestJson);
        manifest.Should().NotBeNull();
        
        // Verify statistics match
        var statistics = manifest!.GetProperty("statistics");
        statistics.GetProperty("shoes").GetInt32().Should().Be(1);
        statistics.GetProperty("workouts").GetInt32().Should().Be(1);
        statistics.GetProperty("bestEfforts").GetInt32().Should().Be(1);
    }

    private async Task<Workout> SeedTestDataAsync()
    {
        var shoe = await TestDataSeeder.SeedShoeAsync(_db);
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, shoeId: shoe.Id);
        // Seed user settings to ensure settings.json is included in export
        await TestDataSeeder.SeedUserSettingsAsync(_db, defaultShoeId: shoe.Id);
        return workout;
    }

    /// <summary>
    /// Cleans the database before a test to ensure accurate statistics
    /// </summary>
    [Fact]
    public async Task ExportAllDataAsync_ShoesJsonIncludesIsRetired()
    {
        await CleanDatabaseAsync();
        await TestDataSeeder.SeedShoeAsync(_db, "Nike", "Active");
        await TestDataSeeder.SeedShoeAsync(_db, "Adidas", "Retired", isRetired: true);

        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var shoesEntry = archive.GetEntry("data/shoes.json");
        shoesEntry.Should().NotBeNull();
        using var shoesStream = shoesEntry!.Open();
        var shoesJson = await new StreamReader(shoesStream).ReadToEndAsync();
        var shoes = JsonSerializer.Deserialize<List<JsonElement>>(shoesJson);
        shoes.Should().NotBeNull();
        shoes!.Should().HaveCount(2);
        var retired = shoes.Single(e => e.GetProperty("brand").GetString() == "Adidas");
        retired.GetProperty("isRetired").GetBoolean().Should().BeTrue();
        var active = shoes.Single(e => e.GetProperty("brand").GetString() == "Nike");
        active.GetProperty("isRetired").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExportAllDataAsync_RoutesJsonDoesNotIncludePreviewGeoJson()
    {
        await CleanDatabaseAsync();
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        var route = await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        route.PreviewGeoJson = TrackGeometry.BuildRoutePreviewGeoJson(route.RouteGeoJson);
        await _db.SaveChangesAsync();

        using var zipStream = new MemoryStream();
        await _exportService.ExportAllDataAsync(zipStream);
        zipStream.Position = 0;

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var routesEntry = archive.GetEntry("data/routes.json");
        routesEntry.Should().NotBeNull();
        using var routesStream = routesEntry!.Open();
        var routesJson = await new StreamReader(routesStream).ReadToEndAsync();
        var routes = JsonSerializer.Deserialize<List<JsonElement>>(routesJson);
        routes.Should().NotBeNull();
        routes!.Should().HaveCount(1);
        routes[0].TryGetProperty("previewGeoJson", out _).Should().BeFalse();
        routes[0].TryGetProperty("PreviewGeoJson", out _).Should().BeFalse();
        routes[0].TryGetProperty("routeGeoJson", out var geoJson).Should().BeTrue();
        geoJson.GetProperty("type").GetString().Should().Be("LineString");
    }

    private async Task CleanDatabaseAsync()
    {
        // Delete in order to respect foreign key constraints
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutTimeSeries");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutSplits");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutMedia");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM BestEfforts");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutRoutes");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Workouts");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserSettings");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Shoes");
    }
}

