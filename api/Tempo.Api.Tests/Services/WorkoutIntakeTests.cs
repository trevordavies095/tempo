using System.Text;
using System.Text.Json;
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
            new SplitHeartRateService(),
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
        await AssertRoutePreviewPersistedAsync(stored.Id);

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
    public async Task ProcessAsync_Updated_WipesDeviceLaps()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var first = CreateGpxStream();
        var created = await _intake.ProcessAsync(first, "morning.gpx");
        created.Action.Should().Be("created");

        var workout = await _db.Workouts.SingleAsync();
        await TestDataSeeder.SeedDeviceLapsAsync(
            _db,
            workout,
            (0, 1600, 600, 0, 650));
        (await _db.WorkoutSplits.CountAsync(s =>
            s.WorkoutId == workout.Id && s.Kind == WorkoutSplitKinds.DeviceLap)).Should().Be(1);

        workout.RawFileData = null;
        await _db.SaveChangesAsync();

        using var second = CreateGpxStream();
        var result = await _intake.ProcessAsync(second, "morning.gpx");

        result.Action.Should().Be("updated");
        var remaining = await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync();
        remaining.Should().NotBeEmpty();
        remaining.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.Distance);
        remaining.Should().NotContain(s => s.Kind == WorkoutSplitKinds.DeviceLap);
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
            await AssertRoutePreviewPersistedAsync(existing.Id);
        }
    }

    [Fact]
    public async Task ProcessAsync_Updated_OverwritesEmptyRouteAndPersistsPreview()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var parseStream = CreateGpxStream();
        var parsed = _gpxParser.ParseGpx(parseStream);

        var existing = new Workout
        {
            StartedAt = parsed.StartTime,
            DurationS = parsed.DurationSeconds,
            DistanceM = parsed.DistanceMeters,
            AvgPaceS = parsed.DurationSeconds / (parsed.DistanceMeters / 1000.0),
            RawFileType = "gpx",
            RawGpxData = "{}",
            Source = "gpx_import",
            RunType = "Easy Run",
            CreatedAt = System.DateTime.UtcNow
        };
        _db.Workouts.Add(existing);
        await _db.SaveChangesAsync();
        _db.WorkoutRoutes.Add(new WorkoutRoute
        {
            WorkoutId = existing.Id,
            RouteGeoJson = ""
        });
        await _db.SaveChangesAsync();

        using var stream = CreateGpxStream();
        var result = await _intake.ProcessAsync(stream, "morning.gpx");

        result.Action.Should().Be("updated");
        result.Workout!.Id.Should().Be(existing.Id);
        var route = await _db.WorkoutRoutes.SingleAsync(r => r.WorkoutId == existing.Id);
        route.RouteGeoJson.Should().NotBeNullOrWhiteSpace();
        await AssertRoutePreviewPersistedAsync(existing.Id);
    }

    [Fact]
    public async Task ProcessAsync_Created_Fit_PersistsRoutePreview()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var fitBytes = CreateMinimalFitBytes();
        using var stream = new MemoryStream(fitBytes);

        var result = await _intake.ProcessAsync(stream, "run.fit");

        result.Action.Should().Be("created");
        result.Workout.Should().NotBeNull();
        await AssertRoutePreviewPersistedAsync(result.Workout!.Id);
    }

    [Fact]
    public async Task ProcessAsync_Created_Fit_StoresTimerAndPaceFromTimer()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var fitBytes = CreateMinimalFitBytes(elapsedSeconds: 1200f, timerSeconds: 1000f);
        using var stream = new MemoryStream(fitBytes);

        var result = await _intake.ProcessAsync(stream, "paused.fit");

        result.Action.Should().Be("created");
        var stored = await _db.Workouts.SingleAsync();
        stored.DurationS.Should().Be(1200);
        stored.TimerTimeS.Should().Be(1000);
        stored.AvgPaceS.Should().BeApproximately(1000 / (stored.DistanceM / 1000.0), 0.01);
    }

    [Fact]
    public async Task ProcessAsync_Created_Fit_WithThreeLaps_WritesDeviceLapsWithTimerAndLeftover()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var fitBytes = CreateMinimalFitBytes(
            elapsedSeconds: 1326f,
            timerSeconds: 1205f,
            totalDistanceMeters: 3231f,
            laps: new[]
            {
                new SyntheticLap(start, 600f, 600f, 1609f, 150, LapTrigger.Distance),
                new SyntheticLap(start.AddSeconds(600), 721f, 600f, 1609f, 155, LapTrigger.Manual),
                new SyntheticLap(start.AddSeconds(1321), 5f, 5f, 13f, null, LapTrigger.SessionEnd),
            });
        using var stream = new MemoryStream(fitBytes);

        var result = await _intake.ProcessAsync(stream, "long-run.fit");

        result.Action.Should().Be("created");
        result.SplitsCount.Should().Be(3);

        var all = await _db.WorkoutSplits.Where(s => s.WorkoutId == result.Workout!.Id).ToListAsync();
        all.Should().Contain(s => s.Kind == WorkoutSplitKinds.Distance);
        var laps = all.Where(s => s.Kind == WorkoutSplitKinds.DeviceLap).OrderBy(s => s.Idx).ToList();
        laps.Should().HaveCount(3);

        laps[0].DurationS.Should().Be(600);
        laps[0].DistanceM.Should().BeApproximately(1609, 0.1);
        laps[0].AvgHeartRateBpm.Should().Be(150);
        laps[0].StartElapsedS.Should().Be(0);
        laps[0].EndElapsedS.Should().Be(600);
        laps[0].StartDistanceM.Should().Be(0);

        laps[1].DurationS.Should().Be(600);
        laps[1].EndElapsedS.Should().Be(1321);
        (laps[1].EndElapsedS - laps[1].StartElapsedS).Should().BeGreaterThan(laps[1].DurationS);
        laps[1].AvgHeartRateBpm.Should().Be(155);
        laps[1].StartDistanceM.Should().BeApproximately(1609, 0.1);

        laps[2].DistanceM.Should().BeApproximately(13, 0.1);
        laps[2].DurationS.Should().Be(5);
        laps[2].StartDistanceM.Should().BeApproximately(3218, 0.1);

        var display = WorkoutSplitDisplay.SelectDisplayList(all);
        display.Should().HaveCount(3);
        display.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.DeviceLap);

        var stored = await _db.Workouts.SingleAsync();
        stored.DurationS.Should().Be(1326);
        stored.RawFitData.Should().Contain("\"laps\"");
    }

    [Fact]
    public async Task ProcessAsync_Created_Fit_WithOneLap_WritesDistanceOnly()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var fitBytes = CreateMinimalFitBytes(
            elapsedSeconds: 1200f,
            timerSeconds: 1200f,
            laps: new[]
            {
                new SyntheticLap(start, 1200f, 1200f, 2400f, null, LapTrigger.SessionEnd),
            });
        using var stream = new MemoryStream(fitBytes);

        var result = await _intake.ProcessAsync(stream, "no-autolap.fit");

        result.Action.Should().Be("created");
        var all = await _db.WorkoutSplits.Where(s => s.WorkoutId == result.Workout!.Id).ToListAsync();
        all.Should().NotBeEmpty();
        all.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.Distance);
        result.SplitsCount.Should().Be(all.Count);
    }

    [Fact]
    public async Task ProcessAsync_Created_Fit_EmptyLapPlusWrapper_WritesDistanceOnly()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var fitBytes = CreateMinimalFitBytes(
            elapsedSeconds: 1200f,
            timerSeconds: 1200f,
            laps: new[]
            {
                new SyntheticLap(start, 0f, 0f, 0f, null, LapTrigger.Manual),
                new SyntheticLap(start, 1200f, 1200f, 2400f, null, LapTrigger.SessionEnd),
            });
        using var stream = new MemoryStream(fitBytes);

        var result = await _intake.ProcessAsync(stream, "junk-wrapper.fit");

        result.Action.Should().Be("created");
        var all = await _db.WorkoutSplits.Where(s => s.WorkoutId == result.Workout!.Id).ToListAsync();
        all.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.Distance);
    }

    [Fact]
    public async Task ProcessAsync_Created_Fit_KeepsTimeAndSessionEndTriggers()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var fitBytes = CreateMinimalFitBytes(
            elapsedSeconds: 900f,
            timerSeconds: 900f,
            totalDistanceMeters: 2500f,
            laps: new[]
            {
                new SyntheticLap(start, 300f, 300f, 800f, null, LapTrigger.Time),
                new SyntheticLap(start.AddSeconds(300), 300f, 300f, 800f, null, LapTrigger.Time),
                new SyntheticLap(start.AddSeconds(600), 300f, 300f, 900f, null, LapTrigger.SessionEnd),
            });
        using var stream = new MemoryStream(fitBytes);

        var result = await _intake.ProcessAsync(stream, "time-laps.fit");

        result.Action.Should().Be("created");
        var laps = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == result.Workout!.Id && s.Kind == WorkoutSplitKinds.DeviceLap)
            .OrderBy(s => s.Idx)
            .ToListAsync();
        laps.Should().HaveCount(3);
        laps.Select(l => l.DistanceM).Should().Equal(800d, 800d, 900d);
    }

    [Fact]
    public async Task ProcessAsync_Created_Gpx_WritesNoDeviceLaps()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var stream = CreateGpxStream();

        var result = await _intake.ProcessAsync(stream, "morning.gpx");

        result.Action.Should().Be("created");
        var all = await _db.WorkoutSplits.Where(s => s.WorkoutId == result.Workout!.Id).ToListAsync();
        all.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.Distance);
    }

    [Fact]
    public async Task ProcessAsync_Updated_Fit_RewritesDeviceLapsFromNewFile()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var fitBytes = CreateMinimalFitBytes(
            elapsedSeconds: 1326f,
            timerSeconds: 1205f,
            totalDistanceMeters: 3231f,
            laps: new[]
            {
                new SyntheticLap(start, 600f, 600f, 1609f, 150, LapTrigger.Distance),
                new SyntheticLap(start.AddSeconds(600), 721f, 600f, 1609f, 155, LapTrigger.Manual),
                new SyntheticLap(start.AddSeconds(1321), 5f, 5f, 13f, null, LapTrigger.SessionEnd),
            });

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
            await TestDataSeeder.SeedDeviceLapsAsync(
                _db,
                existing,
                (0, 100, 60, 0, 60));

            using var stream = new MemoryStream(fitBytes);
            var result = await _intake.ProcessAsync(stream, "run.fit");

            result.Action.Should().Be("updated");
            var laps = await _db.WorkoutSplits
                .Where(s => s.WorkoutId == existing.Id && s.Kind == WorkoutSplitKinds.DeviceLap)
                .OrderBy(s => s.Idx)
                .ToListAsync();
            laps.Should().HaveCount(3);
            laps[0].DistanceM.Should().BeApproximately(1609, 0.1);
            laps[2].DistanceM.Should().BeApproximately(13, 0.1);
            laps.Should().NotContain(l => l.DistanceM == 100);
            (await _db.WorkoutSplits.CountAsync(s =>
                s.WorkoutId == existing.Id && s.Kind == WorkoutSplitKinds.Distance))
                .Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task ProcessAsync_Created_Gpx_LeavesTimerNull()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var stream = CreateGpxStream();

        var result = await _intake.ProcessAsync(stream, "morning.gpx");

        result.Action.Should().Be("created");
        var stored = await _db.Workouts.SingleAsync();
        stored.TimerTimeS.Should().BeNull();
    }

    [Fact]
    public async Task PersistAsync_HealthKit_LeavesTimerNull()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded();

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        var stored = await _db.Workouts.SingleAsync();
        stored.TimerTimeS.Should().BeNull();
        stored.AvgPaceS.Should().BeApproximately(stored.DurationS / (stored.DistanceM / 1000.0), 0.01);
    }

    [Fact]
    public async Task PersistAsync_StravaMoving_NoTimer_PaceFromMoving()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 6, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var decoded = new DecodedWorkout
        {
            StartedAt = start,
            DurationS = 1800,
            DistanceM = 5000,
            TrackPoints = new List<TrackPoint>
            {
                new() { Latitude = 37.7749, Longitude = -122.4194, Time = start },
                new()
                {
                    Latitude = 37.7849,
                    Longitude = -122.4094,
                    Time = start.AddSeconds(1800)
                }
            },
            RawFileType = "gpx",
            RawFileName = "strava.gpx",
            RawFileData = new byte[] { 1 },
            RawGpxDataJson = "{}"
        };
        var overlay = new WorkoutIntakeOverlay
        {
            Source = "strava_import",
            RawStravaDataJson = """{"movingTime":1500}"""
        };

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        var stored = await _db.Workouts.SingleAsync();
        stored.TimerTimeS.Should().BeNull();
        stored.MovingTimeS.Should().Be(1500);
        stored.AvgPaceS.Should().BeApproximately(1500 / 5.0, 0.01);
    }

    [Fact]
    public async Task ProcessAsync_Updated_Fit_FillsTimerAndPaceWithoutChangingDuration()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var fitBytes = CreateMinimalFitBytes(elapsedSeconds: 1200f, timerSeconds: 1000f);
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

            var originalDuration = existing.DurationS;
            using var stream = new MemoryStream(fitBytes);
            var result = await _intake.ProcessAsync(stream, "run.fit");

            result.Action.Should().Be("updated");
            var updated = await _db.Workouts.SingleAsync();
            updated.DurationS.Should().Be(originalDuration);
            updated.TimerTimeS.Should().Be(1000);
            updated.AvgPaceS.Should().BeApproximately(1000 / (updated.DistanceM / 1000.0), 0.01);
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
        stored.HealthKitUuid.Should().Be(Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890"));
        stored.DistanceM.Should().Be(5000);
        stored.DurationS.Should().Be(1800);
        stored.Calories.Should().Be(420);
        stored.Device.Should().Be("Apple Watch");
        (await _db.WorkoutRoutes.CountAsync(r => r.WorkoutId == stored.Id)).Should().Be(1);
        await AssertRoutePreviewPersistedAsync(stored.Id);
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
        (await _db.Workouts.SingleAsync()).HealthKitUuid.Should().Be(Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890"));
        _weather.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_HealthKit_Skipped_WhenSameUuidDifferentStats()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var uuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded(distanceM: 5000, healthKitUuid: uuid);

        var first = await _intake.PersistAsync(decoded, overlay);
        first.Action.Should().Be("created");

        _weather.Reset();
        _relativeEffort.Reset();
        _bestEfforts.Reset();

        // Same UUID, different distance/duration — identity wins over stats.
        var (decoded2, overlay2) = CreateHealthKitOutdoorDecoded(
            startedAt: decoded.StartedAt.AddHours(1),
            durationS: 2400,
            distanceM: 10000,
            healthKitUuid: uuid);
        var second = await _intake.PersistAsync(decoded2, overlay2);

        second.Action.Should().Be("skipped");
        second.Workout!.Id.Should().Be(first.Workout!.Id);
        (await _db.Workouts.CountAsync()).Should().Be(1);
        _weather.CallCount.Should().Be(0);
        _relativeEffort.CallCount.Should().Be(0);
        _bestEfforts.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_HealthKit_Skipped_WhenMatchingGpxAlreadyImported()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var stream = CreateGpxStream();
        var created = await _intake.ProcessAsync(stream, "morning.gpx");
        created.Action.Should().Be("created");
        var gpx = created.Workout!;

        var uuid = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded(
            startedAt: gpx.StartedAt,
            durationS: gpx.DurationS,
            distanceM: gpx.DistanceM,
            healthKitUuid: uuid);

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("skipped");
        result.Workout!.Id.Should().Be(gpx.Id);
        (await _db.Workouts.CountAsync()).Should().Be(1);
        var stored = await _db.Workouts.SingleAsync();
        stored.RawGpxData.Should().NotBeNullOrEmpty();
        stored.RawHealthKitData.Should().BeNull();
        stored.HealthKitUuid.Should().Be(uuid);
    }

    [Fact]
    public async Task PersistAsync_FileImport_DoesNotRequireHealthKitUuid()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        using var stream = CreateGpxStream();

        var result = await _intake.ProcessAsync(stream, "morning.gpx");

        result.Action.Should().Be("created");
        var stored = await _db.Workouts.SingleAsync();
        stored.HealthKitUuid.Should().BeNull();
        (await _db.WorkoutExternalIdentities.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_ExternalIdentity_Created_WritesRow()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateDecodedWithExternalIdentity("  99  ");

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        result.Workout.Should().NotBeNull();
        var stored = await _db.Workouts.SingleAsync();
        stored.Id.Should().Be(result.Workout!.Id);
        var identity = await _db.WorkoutExternalIdentities.SingleAsync();
        identity.WorkoutId.Should().Be(stored.Id);
        identity.Source.Should().Be(WorkoutExternalSource.IntervalsIcu);
        identity.ExternalId.Should().Be("99");
        identity.CreatedAt.Should().BeCloseTo(System.DateTime.UtcNow, TimeSpan.FromSeconds(30));
        _weather.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PersistAsync_ExternalIdentity_Skipped_WhenSamePairPostedTwice()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateDecodedWithExternalIdentity("42");

        var first = await _intake.PersistAsync(decoded, overlay);
        first.Action.Should().Be("created");

        _weather.Reset();
        _relativeEffort.Reset();
        _bestEfforts.Reset();

        var (decoded2, overlay2) = CreateDecodedWithExternalIdentity("42");
        var second = await _intake.PersistAsync(decoded2, overlay2);

        second.Action.Should().Be("skipped");
        second.Workout!.Id.Should().Be(first.Workout!.Id);
        (await _db.Workouts.CountAsync()).Should().Be(1);
        (await _db.WorkoutExternalIdentities.CountAsync()).Should().Be(1);
        _weather.CallCount.Should().Be(0);
        _relativeEffort.CallCount.Should().Be(0);
        _bestEfforts.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_ExternalIdentity_Skipped_WhenSamePairDifferentStats()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateDecodedWithExternalIdentity("7", distanceM: 5000);

        var first = await _intake.PersistAsync(decoded, overlay);
        first.Action.Should().Be("created");

        _weather.Reset();
        _relativeEffort.Reset();
        _bestEfforts.Reset();

        var (decoded2, overlay2) = CreateDecodedWithExternalIdentity(
            "7",
            startedAt: decoded.StartedAt.AddHours(1),
            durationS: 2400,
            distanceM: 10000);
        var second = await _intake.PersistAsync(decoded2, overlay2);

        second.Action.Should().Be("skipped");
        second.Workout!.Id.Should().Be(first.Workout!.Id);
        (await _db.Workouts.CountAsync()).Should().Be(1);
        _weather.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null, "123")]
    [InlineData("intervals_icu", null)]
    [InlineData("  ", "123")]
    [InlineData("intervals_icu", "  ")]
    [InlineData("", "123")]
    public async Task PersistAsync_ExternalIdentity_InvalidOverlay_CreatesWorkoutWithoutRow(
        string? source,
        string? externalId)
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateDecodedWithExternalIdentity("unused");
        overlay = new WorkoutIntakeOverlay
        {
            Source = overlay.Source,
            ExternalIdentity = new WorkoutIntakeExternalIdentity
            {
                Source = source,
                ExternalId = externalId
            }
        };

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        (await _db.Workouts.CountAsync()).Should().Be(1);
        (await _db.WorkoutExternalIdentities.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_ExternalIdentity_DeleteWorkout_CascadesIdentityRows()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateDecodedWithExternalIdentity("cascade");

        var result = await _intake.PersistAsync(decoded, overlay);
        result.Action.Should().Be("created");
        (await _db.WorkoutExternalIdentities.CountAsync()).Should().Be(1);

        _db.Workouts.Remove(result.Workout!);
        await _db.SaveChangesAsync();

        (await _db.Workouts.CountAsync()).Should().Be(0);
        (await _db.WorkoutExternalIdentities.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_ExternalIdentity_ParallelSamePair_YieldsOneWorkout()
    {
        await using var keepAlive = new SqliteConnection("Data Source=file:ext-id-race?mode=memory&cache=shared");
        await keepAlive.OpenAsync();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite("Data Source=file:ext-id-race?mode=memory&cache=shared")
            .Options;

        await using var db1 = new TempoDbContext(options);
        await db1.Database.EnsureCreatedAsync();
        await TestDataSeeder.SeedUserSettingsAsync(db1);

        await using var db2 = new TempoDbContext(options);

        var intake1 = CreateIntake(db1);
        var intake2 = CreateIntake(db2);
        var (decoded1, overlay1) = CreateDecodedWithExternalIdentity("race-1");
        var (decoded2, overlay2) = CreateDecodedWithExternalIdentity("race-1");

        var results = await Task.WhenAll(
            intake1.PersistAsync(decoded1, overlay1),
            intake2.PersistAsync(decoded2, overlay2));

        results.Select(r => r.Action).Should().OnlyContain(a => a == "created" || a == "skipped");
        results.Select(r => r.Action).Should().Contain("created");
        results.Select(r => r.Workout!.Id).Distinct().Should().HaveCount(1);

        await using var verify = new TempoDbContext(options);
        (await verify.Workouts.CountAsync()).Should().Be(1);
        (await verify.WorkoutExternalIdentities.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PersistAsync_HealthKitOutdoor_WithHrSeries_PersistsSplitAvgHeartRate()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded();

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        var splits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == result.Workout!.Id)
            .OrderBy(s => s.Idx)
            .ToListAsync();
        splits.Should().NotBeEmpty();
        splits.Should().Contain(s => s.AvgHeartRateBpm != null);
        splits.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.Distance);
        splits.Should().OnlyContain(s => s.EndElapsedS >= s.StartElapsedS);
        for (var i = 1; i < splits.Count; i++)
        {
            splits[i].StartElapsedS.Should().Be(splits[i - 1].EndElapsedS);
        }
    }

    [Fact]
    public async Task PersistAsync_SessionLevelHrWithoutSeriesHr_LeavesSplitHrNull()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 6, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var decoded = new DecodedWorkout
        {
            StartedAt = start,
            DurationS = 1800,
            DistanceM = 5000,
            TrackPoints =
            [
                new() { Time = start, Latitude = 37.7749, Longitude = -122.4194, DistanceM = 0 },
                new() { Time = start.AddMinutes(15), Latitude = 37.7849, Longitude = -122.4094, DistanceM = 2500 },
                new() { Time = start.AddSeconds(1800), Latitude = 37.7949, Longitude = -122.3994, DistanceM = 5000 }
            ]
        };
        var overlay = new WorkoutIntakeOverlay
        {
            AvgHeartRateBpm = 150,
            MaxHeartRateBpm = 175
        };

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        result.Workout!.AvgHeartRateBpm.Should().Be(150);
        var splits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == result.Workout.Id)
            .ToListAsync();
        splits.Should().NotBeEmpty();
        splits.Should().OnlyContain(s => s.AvgHeartRateBpm == null);
        // GPS present → Haversine path; StartDistanceM stays null even when DistM is on points
        splits.Should().OnlyContain(s => s.StartDistanceM == null);
    }

    [Fact]
    public async Task PersistAsync_HaversineGps_LeavesStartDistanceMNull()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 6, 15, 10, 0, 0, System.DateTimeKind.Utc);
        // GPS points without DistM → Haversine path
        var degreeIncrement = 5000.0 / (111000.0 * 49);
        var points = new List<TrackPoint>();
        for (var i = 0; i < 50; i++)
        {
            points.Add(new TrackPoint
            {
                Time = start.AddSeconds(i * 36),
                Latitude = 37.7749 + i * degreeIncrement,
                Longitude = -122.4194 + i * degreeIncrement
            });
        }

        var result = await _intake.PersistAsync(new DecodedWorkout
        {
            StartedAt = start,
            DurationS = 1800,
            DistanceM = 5000,
            TrackPoints = points,
            RawGpxDataJson = "{}"
        });

        result.Action.Should().Be("created");
        var splits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == result.Workout!.Id)
            .ToListAsync();
        splits.Should().NotBeEmpty();
        splits.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.Distance);
        splits.Should().OnlyContain(s => s.StartDistanceM == null);
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

    [Fact]
    public async Task PersistAsync_HealthKitIndoor_WithHrSeries_PersistsSplitAvgHeartRate()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateHealthKitIndoorDecoded(withDistanceStream: true);

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        var workoutId = result.Workout!.Id;
        var splits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workoutId)
            .OrderBy(s => s.Idx)
            .ToListAsync();
        splits.Should().NotBeEmpty();
        splits.Should().Contain(s => s.AvgHeartRateBpm != null);
        (await _db.WorkoutRoutes.CountAsync(r => r.WorkoutId == workoutId)).Should().Be(0);
        splits.Should().OnlyContain(s => s.StartDistanceM.HasValue);
    }

    [Fact]
    public async Task PersistAsync_HealthKitIndoor_SessionLevelHrWithoutSeriesHr_LeavesSplitHrNull()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 7, 1, 8, 0, 0, System.DateTimeKind.Utc);
        var decoded = new DecodedWorkout
        {
            StartedAt = start,
            DurationS = 1800,
            DistanceM = 5000,
            TrackPoints = Enumerable.Range(0, 50).Select(i =>
            {
                var progress = (double)i / 49;
                return new TrackPoint
                {
                    Time = start.AddSeconds(progress * 1800),
                    DistanceM = progress * 5000
                };
            }).ToList()
        };
        var overlay = new WorkoutIntakeOverlay
        {
            Source = "healthkit",
            Device = "Apple Watch",
            AvgHeartRateBpm = 145,
            MaxHeartRateBpm = 168
        };

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        result.Workout!.AvgHeartRateBpm.Should().Be(145);
        var splits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == result.Workout.Id)
            .ToListAsync();
        splits.Should().NotBeEmpty();
        splits.Should().OnlyContain(s => s.AvgHeartRateBpm == null);
    }

    [Fact]
    public async Task PersistAsync_HealthKitIndoor_WithDistanceStream_PersistsSplitsSeriesNoRoute()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateHealthKitIndoorDecoded(withDistanceStream: true);

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        result.Workout.Should().NotBeNull();
        result.SplitsCount.Should().BeGreaterThan(0);

        var stored = await _db.Workouts.SingleAsync();
        stored.Source.Should().Be("healthkit");
        stored.RawHealthKitData.Should().NotBeNullOrEmpty();
        stored.DistanceM.Should().Be(5000);
        stored.AvgHeartRateBpm.Should().NotBeNull();
        stored.Calories.Should().Be(380);
        stored.Weather.Should().BeNull();
        (await _db.WorkoutRoutes.CountAsync(r => r.WorkoutId == stored.Id)).Should().Be(0);
        (await _db.WorkoutSplits.CountAsync(s => s.WorkoutId == stored.Id)).Should().BeGreaterThan(0);
        (await _db.WorkoutTimeSeries.CountAsync(ts => ts.WorkoutId == stored.Id && ts.HeartRateBpm != null))
            .Should().BeGreaterThan(0);
        (await _db.WorkoutTimeSeries.CountAsync(ts => ts.WorkoutId == stored.Id && ts.DistanceM != null))
            .Should().BeGreaterThan(0);
        _weather.CallCount.Should().Be(0);
        _relativeEffort.CallCount.Should().Be(1);
        _bestEfforts.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PersistAsync_HealthKitIndoor_SummaryOnly_PersistsStatsWithoutRouteSplitsSeries()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var (decoded, overlay) = CreateHealthKitIndoorDecoded(withDistanceStream: false);

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        result.SplitsCount.Should().Be(0);

        var stored = await _db.Workouts.SingleAsync();
        stored.DistanceM.Should().Be(5000);
        stored.DurationS.Should().Be(1800);
        stored.AvgHeartRateBpm.Should().Be(145);
        stored.MaxHeartRateBpm.Should().Be(168);
        stored.Calories.Should().Be(380);
        stored.Weather.Should().BeNull();
        (await _db.WorkoutRoutes.CountAsync(r => r.WorkoutId == stored.Id)).Should().Be(0);
        (await _db.WorkoutSplits.CountAsync(s => s.WorkoutId == stored.Id)).Should().Be(0);
        (await _db.WorkoutTimeSeries.CountAsync(ts => ts.WorkoutId == stored.Id)).Should().Be(0);
        _weather.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_HealthKitIndoor_Skipped_WhenSameUuidPostedTwice()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var uuid = Guid.Parse("DDDDDDDD-EEEE-FFFF-AAAA-BBBBBBBBBBBB");
        var (decoded, overlay) = CreateHealthKitIndoorDecoded(withDistanceStream: true, healthKitUuid: uuid);

        var first = await _intake.PersistAsync(decoded, overlay);
        first.Action.Should().Be("created");

        _weather.Reset();
        var (decoded2, overlay2) = CreateHealthKitIndoorDecoded(withDistanceStream: true, healthKitUuid: uuid);
        var second = await _intake.PersistAsync(decoded2, overlay2);

        second.Action.Should().Be("skipped");
        second.Workout!.Id.Should().Be(first.Workout!.Id);
        (await _db.Workouts.CountAsync()).Should().Be(1);
        _weather.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_HealthKit_WithThreeLaps_WritesDeviceLaps()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 6, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded(
            startedAt: start,
            durationS: 1326,
            distanceM: 3231,
            laps: new[]
            {
                new DeviceLapSummary
                {
                    Timestamp = start.AddSeconds(600),
                    DistanceM = 1609,
                    TimerS = 600,
                    AvgHeartRateBpm = 150
                },
                new DeviceLapSummary
                {
                    Timestamp = start.AddSeconds(1321),
                    DistanceM = 1609,
                    TimerS = 600,
                    AvgHeartRateBpm = 155
                },
                new DeviceLapSummary
                {
                    Timestamp = start.AddSeconds(1326),
                    DistanceM = 13,
                    TimerS = 5
                }
            });

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        result.SplitsCount.Should().Be(3);
        var all = await _db.WorkoutSplits.Where(s => s.WorkoutId == result.Workout!.Id).ToListAsync();
        all.Should().Contain(s => s.Kind == WorkoutSplitKinds.Distance);
        var laps = all.Where(s => s.Kind == WorkoutSplitKinds.DeviceLap).OrderBy(s => s.Idx).ToList();
        laps.Should().HaveCount(3);
        laps[0].DurationS.Should().Be(600);
        laps[0].EndElapsedS.Should().Be(600);
        laps[0].AvgHeartRateBpm.Should().Be(150);
        laps[1].DurationS.Should().Be(600);
        (laps[1].EndElapsedS - laps[1].StartElapsedS).Should().BeGreaterThan(laps[1].DurationS);
        laps[2].DistanceM.Should().BeApproximately(13, 0.1);
        WorkoutSplitDisplay.SelectDisplayList(all).Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.DeviceLap);
    }

    [Fact]
    public async Task PersistAsync_HealthKit_WithOneLap_WritesDistanceOnly()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var start = new System.DateTime(2024, 6, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded(
            startedAt: start,
            laps: new[]
            {
                new DeviceLapSummary
                {
                    Timestamp = start.AddSeconds(1800),
                    DistanceM = 5000,
                    TimerS = 1800
                }
            });

        var result = await _intake.PersistAsync(decoded, overlay);

        result.Action.Should().Be("created");
        var all = await _db.WorkoutSplits.Where(s => s.WorkoutId == result.Workout!.Id).ToListAsync();
        all.Should().OnlyContain(s => s.Kind == WorkoutSplitKinds.Distance);
    }

    [Fact]
    public async Task PersistAsync_HealthKit_UuidRepost_AttachesLapsWithoutGeometryRewrite()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var uuid = Guid.NewGuid();
        var start = new System.DateTime(2024, 6, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded(startedAt: start, healthKitUuid: uuid);

        var first = await _intake.PersistAsync(decoded, overlay);
        first.Action.Should().Be("created");
        var workoutId = first.Workout!.Id;
        var distanceCountBefore = await _db.WorkoutSplits.CountAsync(s =>
            s.WorkoutId == workoutId && s.Kind == WorkoutSplitKinds.Distance);
        var routeBefore = await _db.WorkoutRoutes.SingleAsync(r => r.WorkoutId == workoutId);
        var seriesCountBefore = await _db.WorkoutTimeSeries.CountAsync(ts => ts.WorkoutId == workoutId);
        _weather.Reset();
        _relativeEffort.Reset();
        _bestEfforts.Reset();

        var (decoded2, overlay2) = CreateHealthKitOutdoorDecoded(
            startedAt: start,
            healthKitUuid: uuid,
            rawHealthKitJson: """{"schemaVersion":1,"laps":[{"endedAt":"2024-06-15T10:10:00Z"}]}""",
            laps: new[]
            {
                new DeviceLapSummary
                {
                    Timestamp = start.AddSeconds(600),
                    DistanceM = 1609,
                    TimerS = 600,
                    AvgHeartRateBpm = 150
                },
                new DeviceLapSummary
                {
                    Timestamp = start.AddSeconds(1200),
                    DistanceM = 1609,
                    TimerS = 600
                },
                new DeviceLapSummary
                {
                    Timestamp = start.AddSeconds(1800),
                    DistanceM = 782,
                    TimerS = 600
                }
            });

        var second = await _intake.PersistAsync(decoded2, overlay2);

        second.Action.Should().Be("updated");
        second.Workout!.Id.Should().Be(workoutId);
        var laps = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workoutId && s.Kind == WorkoutSplitKinds.DeviceLap)
            .OrderBy(s => s.Idx)
            .ToListAsync();
        laps.Should().HaveCount(3);
        laps[0].AvgHeartRateBpm.Should().Be(150);
        (await _db.WorkoutSplits.CountAsync(s =>
            s.WorkoutId == workoutId && s.Kind == WorkoutSplitKinds.Distance))
            .Should().Be(distanceCountBefore);
        var routeAfter = await _db.WorkoutRoutes.SingleAsync(r => r.WorkoutId == workoutId);
        routeAfter.RouteGeoJson.Should().Be(routeBefore.RouteGeoJson);
        (await _db.WorkoutTimeSeries.CountAsync(ts => ts.WorkoutId == workoutId))
            .Should().Be(seriesCountBefore);
        var stored = await _db.Workouts.SingleAsync(w => w.Id == workoutId);
        stored.RawHealthKitData.Should().Contain("laps");
        _weather.CallCount.Should().Be(0);
        _relativeEffort.CallCount.Should().Be(0);
        _bestEfforts.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_HealthKit_UuidRepost_WhenDeviceLapsExist_IsSkipped()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var uuid = Guid.NewGuid();
        var start = new System.DateTime(2024, 6, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var (decoded, overlay) = CreateHealthKitOutdoorDecoded(
            startedAt: start,
            healthKitUuid: uuid,
            laps: new[]
            {
                new DeviceLapSummary { Timestamp = start.AddSeconds(600), DistanceM = 1609, TimerS = 600 },
                new DeviceLapSummary { Timestamp = start.AddSeconds(1200), DistanceM = 1609, TimerS = 600 }
            });

        var first = await _intake.PersistAsync(decoded, overlay);
        first.Action.Should().Be("created");
        var lapCount = await _db.WorkoutSplits.CountAsync(s =>
            s.WorkoutId == first.Workout!.Id && s.Kind == WorkoutSplitKinds.DeviceLap);

        var (decoded2, overlay2) = CreateHealthKitOutdoorDecoded(
            startedAt: start,
            healthKitUuid: uuid,
            laps: new[]
            {
                new DeviceLapSummary { Timestamp = start.AddSeconds(300), DistanceM = 800, TimerS = 300 },
                new DeviceLapSummary { Timestamp = start.AddSeconds(600), DistanceM = 800, TimerS = 300 },
                new DeviceLapSummary { Timestamp = start.AddSeconds(900), DistanceM = 800, TimerS = 300 }
            });
        var second = await _intake.PersistAsync(decoded2, overlay2);

        second.Action.Should().Be("skipped");
        (await _db.WorkoutSplits.CountAsync(s =>
            s.WorkoutId == first.Workout!.Id && s.Kind == WorkoutSplitKinds.DeviceLap))
            .Should().Be(lapCount);
    }

    private static WorkoutIntake CreateIntake(TempoDbContext db)
    {
        var elevationConfig = new ElevationCalculationConfig
        {
            NoiseThresholdMeters = 2.0,
            MinDistanceMeters = 10.0
        };
        return new WorkoutIntake(
            db,
            new GpxParserService(elevationConfig),
            new FitParserService(),
            new TrackGeometry(elevationConfig),
            new FakeWeatherService(),
            new HeartRateZoneService(),
            new FakeRelativeEffortService(),
            new FakeBestEffortService(),
            new SplitHeartRateService(),
            NullLogger<WorkoutIntake>.Instance);
    }

    private static (DecodedWorkout Decoded, WorkoutIntakeOverlay Overlay) CreateDecodedWithExternalIdentity(
        string externalId,
        System.DateTime? startedAt = null,
        int durationS = 1800,
        double distanceM = 5000)
    {
        var (decoded, _) = CreateHealthKitOutdoorDecoded(
            startedAt: startedAt,
            durationS: durationS,
            distanceM: distanceM);
        var overlay = new WorkoutIntakeOverlay
        {
            Source = "fit_import",
            ExternalIdentity = new WorkoutIntakeExternalIdentity
            {
                Source = WorkoutExternalSource.IntervalsIcu,
                ExternalId = externalId
            }
        };
        return (decoded, overlay);
    }

    private static (DecodedWorkout Decoded, WorkoutIntakeOverlay Overlay) CreateHealthKitOutdoorDecoded(
        System.DateTime? startedAt = null,
        int durationS = 1800,
        double distanceM = 5000,
        Guid? healthKitUuid = null,
        IReadOnlyList<DeviceLapSummary>? laps = null,
        string? rawHealthKitJson = null)
    {
        var start = startedAt ?? new System.DateTime(2024, 6, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var uuid = healthKitUuid ?? Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
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
            SeriesPoints = null,
            Laps = laps ?? Array.Empty<DeviceLapSummary>()
        };

        var overlay = new WorkoutIntakeOverlay
        {
            Source = "healthkit",
            Device = "Apple Watch",
            HealthKitUuid = uuid,
            RawHealthKitDataJson = rawHealthKitJson ?? $$"""{"schemaVersion":1,"healthKitUuid":"{{uuid}}"}""",
            AvgHeartRateBpm = 150,
            MaxHeartRateBpm = 175,
            EnergyKcal = 420
        };

        return (decoded, overlay);
    }

    private static (DecodedWorkout Decoded, WorkoutIntakeOverlay Overlay) CreateHealthKitIndoorDecoded(
        bool withDistanceStream,
        System.DateTime? startedAt = null,
        int durationS = 1800,
        double distanceM = 5000,
        Guid? healthKitUuid = null)
    {
        var start = startedAt ?? new System.DateTime(2024, 7, 1, 8, 0, 0, System.DateTimeKind.Utc);
        var uuid = healthKitUuid ?? Guid.Parse("B2C3D4E5-F6A7-8901-BCDE-F12345678901");

        List<TrackPoint> trackPoints;
        if (withDistanceStream)
        {
            trackPoints = new List<TrackPoint>();
            for (var i = 0; i < 50; i++)
            {
                var progress = (double)i / 49;
                trackPoints.Add(new TrackPoint
                {
                    Time = start.AddSeconds(progress * durationS),
                    DistanceM = progress * distanceM,
                    HeartRateBpm = (byte)(140 + (i % 20)),
                    CadenceRpm = (byte)(160 + (i % 10))
                });
            }
        }
        else
        {
            trackPoints = new List<TrackPoint>();
        }

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
            HealthKitUuid = uuid,
            RawHealthKitDataJson = "{\"schemaVersion\":1,\"healthKitUuid\":\"" + uuid + "\",\"summary\":{\"isIndoor\":true}}",
            AvgHeartRateBpm = 145,
            MaxHeartRateBpm = 168,
            EnergyKcal = 380
        };

        return (decoded, overlay);
    }

    private async Task AssertRoutePreviewPersistedAsync(Guid workoutId)
    {
        var route = await _db.WorkoutRoutes.SingleAsync(r => r.WorkoutId == workoutId);
        route.PreviewGeoJson.Should().NotBeNull();
        route.PreviewGeoJson.Should().NotBe(TrackGeometry.EmptyRoutePreviewSentinel);
        var preview = JsonSerializer.Deserialize<JsonElement>(route.PreviewGeoJson!);
        preview.GetProperty("type").GetString().Should().Be("LineString");
        var count = preview.GetProperty("coordinates").GetArrayLength();
        count.Should().BeGreaterThan(0);
        count.Should().BeLessThanOrEqualTo(TrackGeometry.RoutePreviewMaxPoints);
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

    private sealed record SyntheticLap(
        System.DateTime Start,
        float ElapsedSeconds,
        float TimerSeconds,
        float DistanceMeters,
        byte? AvgHeartRate,
        LapTrigger Trigger);

    private static byte[] CreateMinimalFitBytes(
        float elapsedSeconds = 1200f,
        float timerSeconds = 1200f,
        float? movingSeconds = null,
        float totalDistanceMeters = 2400f,
        IReadOnlyList<SyntheticLap>? laps = null)
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
            record.SetDistance(i * (totalDistanceMeters / 2f));
            encode.Write(record);
        }

        if (laps != null)
        {
            foreach (var lapDef in laps)
            {
                var lap = new LapMesg();
                lap.SetStartTime(new FitDateTime(lapDef.Start));
                lap.SetTimestamp(new FitDateTime(lapDef.Start.AddSeconds(lapDef.ElapsedSeconds)));
                lap.SetTotalElapsedTime(lapDef.ElapsedSeconds);
                lap.SetTotalTimerTime(lapDef.TimerSeconds);
                lap.SetTotalDistance(lapDef.DistanceMeters);
                if (lapDef.AvgHeartRate.HasValue)
                {
                    lap.SetAvgHeartRate(lapDef.AvgHeartRate.Value);
                }
                lap.SetLapTrigger(lapDef.Trigger);
                encode.Write(lap);
            }
        }

        var session = new SessionMesg();
        session.SetStartTime(fitStart);
        session.SetTimestamp(new FitDateTime(start.AddMinutes(20)));
        session.SetTotalElapsedTime(elapsedSeconds);
        session.SetTotalTimerTime(timerSeconds);
        if (movingSeconds.HasValue)
        {
            session.SetTotalMovingTime(movingSeconds.Value);
        }
        session.SetTotalDistance(totalDistanceMeters);
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
