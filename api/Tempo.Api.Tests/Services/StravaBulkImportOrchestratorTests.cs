using System.IO.Compression;
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

public class StravaBulkImportOrchestratorTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly StravaBulkImportOrchestrator _orchestrator;
    private readonly string _mediaDir;

    public StravaBulkImportOrchestratorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();

        _mediaDir = Path.Combine(Path.GetTempPath(), $"tempo-orch-media-{Guid.NewGuid()}");
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

        var bulk = new BulkImportService(
            _db,
            new StravaCsvParserService(),
            mediaService,
            intake,
            NullLogger<BulkImportService>.Instance);

        _orchestrator = new StravaBulkImportOrchestrator(
            bulk,
            _db,
            NullLogger<StravaBulkImportOrchestrator>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        TryDelete(_mediaDir);
    }

    [Fact]
    public async Task ImportFromZipAsync_OneRunGpx_Successful()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);

        await using var zip = CreateZip(includeCsv: true, includeGpx: true);
        var result = await _orchestrator.ImportFromZipAsync(zip);

        result.TotalProcessed.Should().Be(1);
        result.Successful.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Updated.Should().Be(0);
        result.Errors.Should().Be(0);
        result.ErrorDetails.Should().BeEmpty();
        (await _db.Workouts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportFromZipAsync_MissingActivitiesCsv_Throws()
    {
        await using var zip = CreateZip(includeCsv: false, includeGpx: true);

        var act = async () => await _orchestrator.ImportFromZipAsync(zip);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("ZIP file must contain activities.csv in the root");
        (await _db.Workouts.CountAsync()).Should().Be(0);
    }

    private static MemoryStream CreateZip(bool includeCsv, bool includeGpx)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeCsv)
            {
                var csv = archive.CreateEntry("activities.csv");
                using var writer = new StreamWriter(csv.Open(), Encoding.UTF8);
                writer.WriteLine("Activity ID,Activity Date,Activity Name,Activity Type,Activity Description,Filename,Activity Private Note,Media");
                writer.WriteLine("1,2024-01-15,Morning Run,Run,,activities/morning.gpx,,");
            }

            if (includeGpx)
            {
                var gpx = archive.CreateEntry("activities/morning.gpx");
                using var writer = new StreamWriter(gpx.Open(), Encoding.UTF8);
                writer.Write(GpxContents);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private const string GpxContents = @"<?xml version=""1.0"" encoding=""UTF-8""?>
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
</gpx>";

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
