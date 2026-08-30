using System.Text;
using Dynastream.Fit;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;
using FitDateTime = Dynastream.Fit.DateTime;
using FitFile = Dynastream.Fit.File;

namespace Tempo.Api.Tests.Services;

public class WorkoutIntakeTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly FakeWeatherService _weather;
    private readonly FakeRelativeEffortService _relativeEffort;
    private readonly FakeBestEffortService _bestEfforts;
    private readonly WorkoutIntake _intake;
    private readonly GpxParserService _gpxParser;
    private readonly FitParserService _fitParser;

    public WorkoutIntakeTests()
    {
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
        _fitParser = new FitParserService();
        var trackGeometry = new TrackGeometry(elevationConfig);
        _weather = new FakeWeatherService();
        _relativeEffort = new FakeRelativeEffortService();
        _bestEfforts = new FakeBestEffortService();

        _intake = new WorkoutIntake(
            _db,
            _gpxParser,
            _fitParser,
            trackGeometry,
            _weather,
            new HeartRateZoneService(),
            _relativeEffort,
            _bestEfforts,
            NullLogger<WorkoutIntake>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ProcessAsync_Created_PersistsWorkoutAndInvokesFakes()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var stream = CreateGpxStream();

        var result = await _intake.ProcessAsync(stream, "morning.gpx");

        result.Action.Should().Be("created");
        result.Workout.Should().NotBeNull();
        result.SplitsCount.Should().BeGreaterThan(0);

        var stored = await _db.Workouts.SingleAsync();
        stored.Id.Should().Be(result.Workout!.Id);
        stored.RawFileData.Should().NotBeNullOrEmpty();
        stored.RawGpxData.Should().NotBeNullOrEmpty();
        (await _db.WorkoutRoutes.CountAsync()).Should().Be(1);
        (await _db.WorkoutSplits.CountAsync(s => s.WorkoutId == stored.Id)).Should().Be(result.SplitsCount);

        _weather.CallCount.Should().Be(1);
        _relativeEffort.CallCount.Should().Be(1);
        _bestEfforts.CallCount.Should().Be(1);
        stored.Weather.Should().Be("{\"source\":\"fake\"}");
        stored.RelativeEffort.Should().Be(7);
    }

    [Fact]
    public async Task ProcessAsync_Updated_WhenRawBytesMissing()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var first = CreateGpxStream();
        var created = await _intake.ProcessAsync(first, "morning.gpx");
        created.Action.Should().Be("created");

        var workout = await _db.Workouts.SingleAsync();
        var originalDistance = workout.DistanceM;
        var originalDuration = workout.DurationS;
        var originalElev = workout.ElevGainM;
        workout.RawFileData = null;
        await _db.SaveChangesAsync();

        _weather.Reset();
        _relativeEffort.Reset();
        _bestEfforts.Reset();

        using var second = CreateGpxStream();
        var result = await _intake.ProcessAsync(second, "morning.gpx");

        result.Action.Should().Be("updated");
        result.Workout!.Id.Should().Be(workout.Id);
        var updated = await _db.Workouts.SingleAsync();
        updated.RawFileData.Should().NotBeNullOrEmpty();
        updated.DistanceM.Should().Be(originalDistance);
        updated.DurationS.Should().Be(originalDuration);
        updated.ElevGainM.Should().Be(originalElev);
        _weather.CallCount.Should().Be(0);
        _relativeEffort.CallCount.Should().Be(0);
        _bestEfforts.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_Updated_WhenFitJsonMissingTrackPoints()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var fitBytes = CreateMinimalFitBytes();
        using (var parseStream = new MemoryStream(fitBytes))
        {
            var parsed = _fitParser.ParseFit(parseStream);
            var existing = new Workout
            {
                StartedAt = parsed.StartTime,
                DurationS = parsed.DurationSeconds,
                DistanceM = parsed.DistanceMeters,
                AvgPaceS = parsed.DurationSeconds / (parsed.DistanceMeters / 1000.0),
                ElevGainM = 42,
                RawFileData = new byte[] { 1, 2, 3 },
                RawFileName = "old.fit",
                RawFileType = "fit",
                RawFitData = """{"session":{}}""",
                Source = "fit_import",
                RunType = "Easy Run",
                CreatedAt = System.DateTime.UtcNow
            };
            _db.Workouts.Add(existing);
            await _db.SaveChangesAsync();

            using var stream = new MemoryStream(fitBytes);
            var result = await _intake.ProcessAsync(stream, "run.fit");

            result.Action.Should().Be("updated");
            result.Workout!.Id.Should().Be(existing.Id);
            var updated = await _db.Workouts.SingleAsync();
            updated.DistanceM.Should().Be(parsed.DistanceMeters);
            updated.DurationS.Should().Be(parsed.DurationSeconds);
            updated.ElevGainM.Should().Be(42);
            updated.RawFitData.Should().Contain("trackPoints");
        }
    }

    [Fact]
    public async Task ProcessAsync_Skipped_WhenRawDataComplete()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var first = CreateGpxStream();
        var created = await _intake.ProcessAsync(first, "morning.gpx");
        created.Action.Should().Be("created");
        var originalId = created.Workout!.Id;

        _weather.Reset();
        _relativeEffort.Reset();
        _bestEfforts.Reset();

        using var second = CreateGpxStream();
        var result = await _intake.ProcessAsync(second, "morning.gpx");

        result.Action.Should().Be("skipped");
        result.Workout!.Id.Should().Be(originalId);
        (await _db.Workouts.CountAsync()).Should().Be(1);
        _weather.CallCount.Should().Be(0);
        _bestEfforts.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_Error_WhenStreamEmpty()
    {
        var result = await _intake.ProcessAsync(new MemoryStream(), "morning.gpx");

        result.Action.Should().Be("error");
        result.ErrorMessage.Should().Be("File is empty");
        (await _db.Workouts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_Error_WhenUnsupportedFilename()
    {
        using var stream = CreateGpxStream();
        var result = await _intake.ProcessAsync(stream, "notes.txt");

        result.Action.Should().Be("error");
        result.ErrorMessage.Should().Be("File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)");
        (await _db.Workouts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_Created_FromDecodedWorkoutWithoutFileAdapter()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var stream = CreateGpxStream();
        var rawFileData = stream.ToArray();
        stream.Position = 0;
        var parsed = _gpxParser.ParseGpx(stream);

        var decoded = new DecodedWorkout
        {
            StartedAt = parsed.StartTime,
            DurationS = parsed.DurationSeconds,
            DistanceM = parsed.DistanceMeters,
            TrackPoints = parsed.TrackPoints,
            SeriesPoints = null,
            Name = parsed.Name,
            RawGpxDataJson = parsed.RawGpxDataJson,
            RawFileData = rawFileData,
            RawFileName = "morning.gpx",
            RawFileType = "gpx"
        };

        var result = await _intake.PersistAsync(decoded);

        result.Action.Should().Be("created");
        result.Workout.Should().NotBeNull();
        result.SplitsCount.Should().BeGreaterThan(0);
        (await _db.Workouts.CountAsync()).Should().Be(1);
        _weather.CallCount.Should().Be(1);
        _relativeEffort.CallCount.Should().Be(1);
        _bestEfforts.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PersistAsync_HealthKit_Created_PersistsRouteSplitsSeriesAndRawJson()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded();

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        result.Workout.Should().NotBeNull();
        result.SplitsCount.Should().BeGreaterThan(0);

        var stored = await _db.Workouts.SingleAsync();
        stored.Source.Should().Be("healthkit");
        stored.RawHealthKitData.Should().NotBeNullOrEmpty();
        stored.DistanceM.Should().Be(5000);
        stored.DurationS.Should().Be(1800);
        stored.Calories.Should().Be(420);
        stored.Device.Should().Be("Apple Watch");
        (await _db.WorkoutRoutes.CountAsync(r => r.WorkoutId == stored.Id)).Should().Be(1);
        (await _db.WorkoutTimeSeries.CountAsync(ts => ts.WorkoutId == stored.Id && ts.HeartRateBpm != null))
            .Should().BeGreaterThan(0);
        _weather.CallCount.Should().Be(1);
        _relativeEffort.CallCount.Should().Be(1);
        _bestEfforts.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PersistAsync_HealthKit_Skipped_WhenSameStatsPostedTwice()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded();

        var first = await _intake.PersistAsync(decoded, overlay);
        first.Action.Should().Be("created");

        _weather.Reset();
        _relativeEffort.Reset();
        _bestEfforts.Reset();

        var (decoded2, overlay2) = CreateHealthKitOutdoorDecoded();
        var second = await _intake.PersistAsync(decoded2, overlay2);

        second.Action.Should().Be("skipped");
        second.Workout!.Id.Should().Be(first.Workout!.Id);
        (await _db.Workouts.CountAsync()).Should().Be(1);
        _weather.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_HealthKit_Skipped_WhenMatchingGpxAlreadyImported()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var stream = CreateGpxStream();
        var created = await _intake.ProcessAsync(stream, "morning.gpx");
        created.Action.Should().Be("created");
        var gpx = created.Workout!;

        var (decoded, overlay) = CreateHealthKitOutdoorDecoded(
            startedAt: gpx.StartedAt,
            durationS: gpx.DurationS,
            distanceM: gpx.DistanceM);

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("skipped");
        result.Workout!.Id.Should().Be(gpx.Id);
        (await _db.Workouts.CountAsync()).Should().Be(1);
        var stored = await _db.Workouts.SingleAsync();
        stored.RawGpxData.Should().NotBeNullOrEmpty();
        stored.RawHealthKitData.Should().BeNull();
    }

    [Fact]
    public async Task PersistAsync_HealthKit_UsesSummaryDistanceOverGpsSpan()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        // Track points span ~3km GPS but summary says 5000m — summary wins.
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded(distanceM: 5000);

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        result.Workout!.DistanceM.Should().Be(5000);
        result.Workout.DurationS.Should().Be(1800);
    }

    private static (DecodedWorkout Decoded, WorkoutIntakeOverlay Overlay) CreateHealthKitOutdoorDecoded(
        System.DateTime? startedAt = null,
        int durationS = 1800,
        double distanceM = 5000)
    {
        var start = startedAt ?? new System.DateTime(2024, 6, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var trackPoints = new List<TrackPoint>
        {
            new()
            {
                Time = start,
                Latitude = 37.7749,
                Longitude = -122.4194,
                Elevation = 10,
                HeartRateBpm = 140,
                CadenceRpm = 160,
                PowerWatts = 250,
                DistanceM = 0
            },
            new()
            {
                Time = start.AddMinutes(15),
                Latitude = 37.7849,
                Longitude = -122.4094,
                Elevation = 25,
                HeartRateBpm = 155,
                CadenceRpm = 165,
                PowerWatts = 270,
                DistanceM = distanceM / 2
            },
            new()
            {
                Time = start.AddSeconds(durationS),
                Latitude = 37.7949,
                Longitude = -122.3994,
                Elevation = 40,
                HeartRateBpm = 160,
                CadenceRpm = 168,
                PowerWatts = 280,
                DistanceM = distanceM
            }
        };

        var decoded = new DecodedWorkout
        {
            StartedAt = start,
            DurationS = durationS,
            DistanceM = distanceM,
            TrackPoints = trackPoints,
            SeriesPoints = null
        };

        var overlay = new WorkoutIntakeOverlay
        {
            Source = "healthkit",
            Device = "Apple Watch",
            RawHealthKitDataJson = """{"schemaVersion":1,"healthKitUuid":"test-uuid"}""",
            AvgHeartRateBpm = 150,
            MaxHeartRateBpm = 175,
            EnergyKcal = 420
        };

        return (decoded, overlay);
    }

    private static MemoryStream CreateGpxStream()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
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
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }

    private static byte[] CreateMinimalFitBytes()
    {
        var start = new System.DateTime(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var fitStart = new FitDateTime(start);
        const double semicirclesPerDegree = 2147483648.0 / 180.0;

        using var stream = new MemoryStream();
        var encode = new Encode(stream, ProtocolVersion.V20);

        var fileId = new FileIdMesg();
        fileId.SetType(FitFile.Activity);
        fileId.SetTimeCreated(fitStart);
        encode.Write(fileId);

        for (var i = 0; i < 3; i++)
        {
            var record = new RecordMesg();
            var time = new FitDateTime(start.AddMinutes(i * 10));
            record.SetTimestamp(time);
            record.SetPositionLat((int)((37.7749 + i * 0.01) * semicirclesPerDegree));
            record.SetPositionLong((int)((-122.4194 + i * 0.01) * semicirclesPerDegree));
            record.SetAltitude(10f + i * 10f);
            record.SetDistance(i * 1200f);
            encode.Write(record);
        }

        var session = new SessionMesg();
        session.SetStartTime(fitStart);
        session.SetTimestamp(new FitDateTime(start.AddMinutes(20)));
        session.SetTotalElapsedTime(1200f);
        session.SetTotalTimerTime(1200f);
        session.SetTotalDistance(2400f);
        session.SetSport(Sport.Running);
        encode.Write(session);
        encode.Close();

        return stream.ToArray();
    }

    private sealed class FakeWeatherService : IWeatherService
    {
        public int CallCount { get; private set; }

        public Task<string?> GetWeatherForWorkoutAsync(
            string? rawStravaDataJson,
            string? rawFitDataJson,
            double? latitude,
            double? longitude,
            System.DateTime startTime)
        {
            CallCount++;
            return Task.FromResult<string?>("{\"source\":\"fake\"}");
        }

        public void Reset() => CallCount = 0;
    }

    private sealed class FakeRelativeEffortService : IRelativeEffortService
    {
        public int CallCount { get; private set; }

        public int? CalculateRelativeEffort(Workout workout, List<HeartRateZone> zones, TempoDbContext db)
        {
            CallCount++;
            return 7;
        }

        public void Reset() => CallCount = 0;
    }

    private sealed class FakeBestEffortService : IBestEffortService
    {
        public int CallCount { get; private set; }

        public Task UpdateBestEffortsForNewWorkoutAsync(TempoDbContext db, Workout workout)
        {
            CallCount++;
            return Task.CompletedTask;
        }

        public void Reset() => CallCount = 0;
    }
}
