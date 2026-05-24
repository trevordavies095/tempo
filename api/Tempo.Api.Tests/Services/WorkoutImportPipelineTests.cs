using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Tests for duplicate handling in <see cref="WorkoutImportPipeline"/>.
/// </summary>
public class WorkoutImportPipelineTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly GpxParserService _gpxParser;
    private readonly WorkoutImportPipeline _pipeline;
    private readonly byte[] _gpxBytes;

    public WorkoutImportPipelineTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _db = new TempoDbContext(new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();

        var elevationConfig = new ElevationCalculationConfig
        {
            NoiseThresholdMeters = 2.0,
            MinDistanceMeters = 10.0
        };
        _gpxParser = new GpxParserService(elevationConfig);

        var fitParser = new FitParserService(elevationConfig);
        var weatherService = new WeatherService(new HttpClient(), NullLogger<WeatherService>.Instance);

        _pipeline = new WorkoutImportPipeline(
            _db,
            _gpxParser,
            fitParser,
            weatherService,
            NullLogger<WorkoutImportPipeline>.Instance);

        _gpxBytes = Encoding.UTF8.GetBytes(CreateTestGpxXml());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task RunAsync_DuplicateWithRawFile_SkipsWhenJsonBackfillDisabled()
    {
        var parsed = _gpxParser.ParseGpx(new MemoryStream(_gpxBytes));
        var startedAtUtc = DateTime.SpecifyKind(parsed.StartTime, DateTimeKind.Utc);

        var existing = new Workout
        {
            Id = Guid.NewGuid(),
            StartedAt = startedAtUtc,
            DurationS = parsed.DurationSeconds,
            DistanceM = parsed.DistanceMeters,
            AvgPaceS = parsed.DurationSeconds / (parsed.DistanceMeters / 1000.0),
            RawFileData = _gpxBytes,
            RawFileName = "existing.gpx",
            RawFileType = "gpx",
            RawGpxData = null,
            Source = "gpx_import",
            CreatedAt = DateTime.UtcNow
        };
        _db.Workouts.Add(existing);
        await _db.SaveChangesAsync();

        var options = new WorkoutImportPipeline.ImportOptions(
            SplitDistanceMeters: 1000.0,
            BackfillMissingRawJsonOnDuplicate: false);

        var result = await _pipeline.RunAsync(
            new WorkoutImportPipeline.ImportInput(_gpxBytes, "reimport.gpx", options));

        result.Should().BeOfType<WorkoutImportPipeline.Skipped>();
        ((WorkoutImportPipeline.Skipped)result).ExistingWorkoutId.Should().Be(existing.Id);
        (await _db.WorkoutSplits.CountAsync(s => s.WorkoutId == existing.Id)).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_DuplicateWithRawFile_UpdatesJsonWhenBackfillEnabled()
    {
        var parsed = _gpxParser.ParseGpx(new MemoryStream(_gpxBytes));
        var startedAtUtc = DateTime.SpecifyKind(parsed.StartTime, DateTimeKind.Utc);

        var existing = new Workout
        {
            Id = Guid.NewGuid(),
            StartedAt = startedAtUtc,
            DurationS = parsed.DurationSeconds,
            DistanceM = parsed.DistanceMeters,
            AvgPaceS = parsed.DurationSeconds / (parsed.DistanceMeters / 1000.0),
            RawFileData = _gpxBytes,
            RawFileName = "existing.gpx",
            RawFileType = "gpx",
            RawGpxData = null,
            Source = "gpx_import",
            CreatedAt = DateTime.UtcNow
        };
        _db.Workouts.Add(existing);
        await _db.SaveChangesAsync();

        var options = new WorkoutImportPipeline.ImportOptions(
            SplitDistanceMeters: 1000.0,
            BackfillMissingRawJsonOnDuplicate: true);

        var result = await _pipeline.RunAsync(
            new WorkoutImportPipeline.ImportInput(_gpxBytes, "reimport.gpx", options));

        result.Should().BeOfType<WorkoutImportPipeline.Updated>();
        existing.RawGpxData.Should().NotBeNullOrEmpty();
    }

    private static string CreateTestGpxXml()
    {
        var start = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var points = new[]
        {
            (37.7749, -122.4194, start),
            (37.7849, -122.4094, start.AddMinutes(10)),
            (37.7949, -122.3994, start.AddMinutes(20)),
            (37.8049, -122.3894, start.AddMinutes(30)),
        };

        var trkpts = string.Join("\n", points.Select(p =>
            $@"      <trkpt lat=""{p.Item1}"" lon=""{p.Item2}"">
        <ele>10</ele>
        <time>{p.Item3:yyyy-MM-ddTHH:mm:ss}Z</time>
      </trkpt>"));

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk><trkseg>
{trkpts}
  </trkseg></trk>
</gpx>";
    }
}
