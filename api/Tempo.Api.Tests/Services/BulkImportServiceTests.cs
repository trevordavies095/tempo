using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class BulkImportServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly BulkImportService _bulk;
    private readonly string _tempDir;
    private readonly string _mediaDir;

    public BulkImportServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();

        _tempDir = Path.Combine(Path.GetTempPath(), $"tempo-bulk-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "activities"));

        _mediaDir = Path.Combine(Path.GetTempPath(), $"tempo-bulk-media-{Guid.NewGuid()}");
        Directory.CreateDirectory(_mediaDir);

        var elevationConfig = new ElevationCalculationConfig
        {
            NoiseThresholdMeters = 2.0,
            MinDistanceMeters = 10.0
        };

        var intake = new WorkoutIntake(
            _db,
            new GpxParserService(elevationConfig),
            new FitParserService(),
            new TrackGeometry(elevationConfig),
            new FakeWeatherService(),
            new HeartRateZoneService(),
            new FakeRelativeEffortService(),
            new FakeBestEffortService(),
            NullLogger<WorkoutIntake>.Instance);

        var mediaService = new MediaService(
            new MediaStorageConfig { RootPath = _mediaDir, MaxFileSizeBytes = 52_428_800 },
            NullLogger<MediaService>.Instance);

        _bulk = new BulkImportService(
            _db,
            new StravaCsvParserService(),
            mediaService,
            intake,
            NullLogger<BulkImportService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        TryDelete(_tempDir);
        TryDelete(_mediaDir);
    }

    [Fact]
    public async Task ProcessActivityFileAsync_PathTraversal_ReturnsInvalidPath()
    {
        var activity = new StravaCsvParserService.StravaActivityRecord
        {
            Filename = "../outside.gpx",
            ActivityType = "Run"
        };

        var result = await _bulk.ProcessActivityFileAsync(activity, _tempDir);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Invalid file path detected");
    }

    [Fact]
    public async Task ProcessActivityFileAsync_MissingFile_ReturnsNotFound()
    {
        var activity = new StravaCsvParserService.StravaActivityRecord
        {
            Filename = "activities/missing.gpx",
            ActivityType = "Run"
        };

        var result = await _bulk.ProcessActivityFileAsync(activity, _tempDir);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("File not found in ZIP archive");
    }

    [Fact]
    public void GetRunActivities_SkipsNonRuns()
    {
        var activities = new List<StravaCsvParserService.StravaActivityRecord>
        {
            new() { ActivityType = "Run", Filename = "activities/a.gpx" },
            new() { ActivityType = "Ride", Filename = "activities/b.gpx" },
            new() { ActivityType = "run", Filename = "activities/c.gpx" }
        };

        var runs = _bulk.GetCsvParser().GetRunActivities(activities);

        runs.Should().HaveCount(2);
        runs.Should().OnlyContain(a => a.ActivityType.Equals("Run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProcessActivityFileAsync_Created_AppliesStravaOverlayAndPersists()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        WriteGpx("activities/morning.gpx");

        var activity = new StravaCsvParserService.StravaActivityRecord
        {
            ActivityName = "Strava Morning",
            ActivityType = "Run",
            Filename = "activities/morning.gpx",
            ActivityDescription = "Easy miles",
            Media = "media/photo.jpg"
        };

        var result = await _bulk.ProcessActivityFileAsync(activity, _tempDir);

        result.Success.Should().BeTrue();
        result.Action.Should().Be("created");
        result.Workout.Should().NotBeNull();
        result.Workout!.Name.Should().Be("Strava Morning");
        result.Workout.Source.Should().Be("strava_import");
        result.Workout.Notes.Should().Be("Easy miles");
        result.MediaPaths.Should().Contain("media/photo.jpg");
        (await _db.Workouts.CountAsync()).Should().Be(1);
        (await _db.Workouts.SingleAsync()).Id.Should().Be(result.Workout.Id);
    }

    [Fact]
    public async Task ProcessActivityFileAsync_CompleteDuplicate_SkippedWithMediaPaths()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        WriteGpx("activities/morning.gpx");

        var activity = new StravaCsvParserService.StravaActivityRecord
        {
            ActivityName = "Strava Morning",
            ActivityType = "Run",
            Filename = "activities/morning.gpx",
            Media = "media/photo.jpg"
        };

        var created = await _bulk.ProcessActivityFileAsync(activity, _tempDir);
        created.Action.Should().Be("created");

        var skipped = await _bulk.ProcessActivityFileAsync(activity, _tempDir);

        skipped.Success.Should().BeTrue();
        skipped.Action.Should().Be("skipped");
        skipped.Workout!.Id.Should().Be(created.Workout!.Id);
        skipped.MediaPaths.Should().Contain("media/photo.jpg");
        (await _db.Workouts.CountAsync()).Should().Be(1);

        Directory.CreateDirectory(Path.Combine(_tempDir, "media"));
        var photoPath = Path.Combine(_tempDir, "media", "photo.jpg");
        await File.WriteAllBytesAsync(photoPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        var firstMedia = await _bulk.ProcessMediaFilesAsync(skipped.Workout.Id, skipped.MediaPaths, _tempDir);
        firstMedia.Should().HaveCount(1);

        _db.WorkoutMedia.AddRange(firstMedia);
        await _db.SaveChangesAsync();

        var secondMedia = await _bulk.ProcessMediaFilesAsync(skipped.Workout.Id, skipped.MediaPaths, _tempDir);
        secondMedia.Should().BeEmpty();
    }

    private void WriteGpx(string relativePath)
    {
        var fullPath = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <name>Morning Run</name>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <ele>10</ele>
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt lat=""37.7849"" lon=""-122.4094"">
        <ele>20</ele>
        <time>2024-01-15T10:10:00Z</time>
      </trkpt>
      <trkpt lat=""37.7949"" lon=""-122.3994"">
        <ele>30</ele>
        <time>2024-01-15T10:20:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>", Encoding.UTF8);
    }

    private static void TryDelete(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // ignore cleanup errors
        }
    }

    private sealed class FakeWeatherService : IWeatherService
    {
        public Task<string?> GetWeatherForWorkoutAsync(
            string? rawStravaDataJson,
            string? rawFitDataJson,
            double? latitude,
            double? longitude,
            DateTime startTime) => Task.FromResult<string?>(null);
    }

    private sealed class FakeRelativeEffortService : IRelativeEffortService
    {
        public int? CalculateRelativeEffort(Workout workout, List<HeartRateZone> zones, TempoDbContext db) => null;
    }

    private sealed class FakeBestEffortService : IBestEffortService
    {
        public Task UpdateBestEffortsForNewWorkoutAsync(TempoDbContext db, Workout workout) => Task.CompletedTask;
    }
}
