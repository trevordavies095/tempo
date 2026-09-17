using System.Text.Json;
using Dynastream.Fit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;
using FitDateTime = Dynastream.Fit.DateTime;
using FitFile = Dynastream.Fit.File;

namespace Tempo.Api.Tests.Services;

public class DeviceLapBackfillServiceTests : IAsyncLifetime
{
    private static readonly System.DateTime FixtureStart =
        new(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);

    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ListLogger<DeviceLapBackfillService> _logger = null!;
    private DeviceLapBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _logger = new ListLogger<DeviceLapBackfillService>();
        _service = new DeviceLapBackfillService(
            _db,
            new FitParserService(),
            new SplitHeartRateService(),
            _logger);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        if (_cloneConnectionString is not null)
        {
            await PostgresTestFixture.DropCloneAsync(_cloneConnectionString);
        }
    }

    [Fact]
    public async Task RunAsync_IsNoOp_WhenNoCandidates()
    {
        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        _logger.Messages.Should().Contain("Device lap backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_AppliesFromJsonLaps_InsertsDeviceLaps_LeavesDistanceUnchanged()
    {
        var workout = await SeedFitCandidateAsync(durationS: 1800);
        var distanceSplits = await TestDataSeeder.SeedWorkoutWithSplitsAsync(_db, workout, splitDistanceM: 1000);
        workout.RawFitData = BuildRawFitWithLaps(
            sessionElapsed: 1800,
            laps:
            [
                LapJson(FixtureStart, distance: 1000, timer: 300, elapsed: 300, hr: 140),
                LapJson(FixtureStart.AddSeconds(300), distance: 1000, timer: 310, elapsed: 310, hr: 145),
                LapJson(FixtureStart.AddSeconds(610), distance: 500, timer: 200, elapsed: 200, hr: 150)
            ]);
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        await _db.Entry(workout).ReloadAsync();
        Marker(workout.RawFitData!).Should().Be(DeviceLapBackfillService.MarkerApplied);

        var deviceLaps = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workout.Id && s.Kind == WorkoutSplitKinds.DeviceLap)
            .OrderBy(s => s.Idx)
            .ToListAsync();
        deviceLaps.Should().HaveCount(3);
        deviceLaps[0].DistanceM.Should().Be(1000);
        deviceLaps[0].DurationS.Should().Be(300);
        deviceLaps[0].AvgHeartRateBpm.Should().Be(140);

        var distanceAfter = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workout.Id && s.Kind == WorkoutSplitKinds.Distance)
            .OrderBy(s => s.Idx)
            .ToListAsync();
        distanceAfter.Should().HaveCount(distanceSplits.Count);
        distanceAfter.Select(s => s.Id).Should().BeEquivalentTo(distanceSplits.Select(s => s.Id));
    }

    [Fact]
    public async Task RunAsync_ReparsesFitBytes_WhenLapsMissingFromJson()
    {
        var start = FixtureStart;
        var fitBytes = CreateFitWithLaps(
            elapsedSeconds: 900f,
            timerSeconds: 880f,
            laps:
            [
                new SyntheticLap(start, 300f, 290f, 1000f, 142, LapTrigger.Distance),
                new SyntheticLap(start.AddSeconds(300), 300f, 295f, 1000f, 148, LapTrigger.Distance),
                new SyntheticLap(start.AddSeconds(600), 300f, 295f, 800f, 151, LapTrigger.Manual)
            ]);

        var workout = await SeedFitCandidateAsync(durationS: 900, distanceM: 2800);
        workout.RawFileData = fitBytes;
        workout.RawFileName = "multi-lap.fit";
        workout.RawFileType = "fit";
        workout.RawFitData = """{"session":{"totalElapsedTime":900},"source":"fit_import"}""";
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        await _db.Entry(workout).ReloadAsync();
        Marker(workout.RawFitData!).Should().Be(DeviceLapBackfillService.MarkerApplied);

        using var doc = JsonDocument.Parse(workout.RawFitData!);
        doc.RootElement.GetProperty("laps").GetArrayLength().Should().Be(3);

        var deviceLaps = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workout.Id && s.Kind == WorkoutSplitKinds.DeviceLap)
            .ToListAsync();
        deviceLaps.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunAsync_StampsSkippedCrop_WhenSessionElapsedDiffersFromDuration()
    {
        var workout = await SeedFitCandidateAsync(durationS: 1000);
        workout.RawFitData = BuildRawFitWithLaps(
            sessionElapsed: 1800,
            laps:
            [
                LapJson(FixtureStart, distance: 1000, timer: 300, elapsed: 300),
                LapJson(FixtureStart.AddSeconds(300), distance: 1000, timer: 300, elapsed: 300)
            ]);
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        await _db.Entry(workout).ReloadAsync();
        Marker(workout.RawFitData!).Should().Be(DeviceLapBackfillService.MarkerSkippedCrop);
        (await DeviceLapCountAsync(workout.Id)).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_StampsSkippedSingle_WhenOnlyOneUsableLap()
    {
        var workout = await SeedFitCandidateAsync(durationS: 600);
        workout.RawFitData = BuildRawFitWithLaps(
            sessionElapsed: 600,
            laps:
            [
                LapJson(FixtureStart, distance: 1000, timer: 300, elapsed: 300),
                LapJson(FixtureStart.AddSeconds(300), distance: 0, timer: 0, elapsed: 0)
            ]);
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        await _db.Entry(workout).ReloadAsync();
        Marker(workout.RawFitData!).Should().Be(DeviceLapBackfillService.MarkerSkippedSingle);
        (await DeviceLapCountAsync(workout.Id)).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_StampsSkippedEmpty_WhenLapMessagesPresentButNoneKept()
    {
        var workout = await SeedFitCandidateAsync(durationS: 600);
        workout.RawFitData = BuildRawFitWithLaps(
            sessionElapsed: 600,
            laps:
            [
                LapJson(FixtureStart, distance: 0, timer: 0, elapsed: 0),
                LapJson(FixtureStart.AddSeconds(300), distance: 0, timer: null, elapsed: 0)
            ]);
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        await _db.Entry(workout).ReloadAsync();
        Marker(workout.RawFitData!).Should().Be(DeviceLapBackfillService.MarkerSkippedEmpty);
        (await DeviceLapCountAsync(workout.Id)).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_StampsAbsent_WhenNoLapMessages()
    {
        var workout = await SeedFitCandidateAsync(durationS: 1200);
        workout.RawFitData = """{"session":{"totalElapsedTime":1200},"source":"fit_import"}""";
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        await _db.Entry(workout).ReloadAsync();
        Marker(workout.RawFitData!).Should().Be(DeviceLapBackfillService.MarkerAbsent);
        (await DeviceLapCountAsync(workout.Id)).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_SecondRun_IsNoOp_ForMarkedAndAlreadyLapped()
    {
        var marked = await SeedFitCandidateAsync(durationS: 1800, name: "Marked");
        marked.RawFitData = BuildRawFitWithLaps(
            sessionElapsed: 1800,
            laps:
            [
                LapJson(FixtureStart, distance: 1000, timer: 300, elapsed: 300),
                LapJson(FixtureStart.AddSeconds(300), distance: 1000, timer: 300, elapsed: 300)
            ]);
        await _db.SaveChangesAsync();

        (await _service.RunAsync()).Should().Be(1);

        var already = await SeedFitCandidateAsync(durationS: 900, name: "Already lapped");
        already.RawFitData = BuildRawFitWithLaps(
            sessionElapsed: 900,
            laps:
            [
                LapJson(FixtureStart, distance: 500, timer: 200, elapsed: 200),
                LapJson(FixtureStart.AddSeconds(200), distance: 500, timer: 200, elapsed: 200)
            ]);
        await TestDataSeeder.SeedDeviceLapsAsync(
            _db,
            already,
            (0, 500, 200, 0, 200),
            (1, 500, 200, 200, 400));
        await _db.SaveChangesAsync();

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Device lap backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_SkipsWorkoutThatAlreadyHasDeviceLaps()
    {
        var workout = await SeedFitCandidateAsync(durationS: 900);
        workout.RawFitData = BuildRawFitWithLaps(
            sessionElapsed: 900,
            laps:
            [
                LapJson(FixtureStart, distance: 1000, timer: 300, elapsed: 300),
                LapJson(FixtureStart.AddSeconds(300), distance: 1000, timer: 300, elapsed: 300)
            ]);
        await TestDataSeeder.SeedDeviceLapsAsync(
            _db,
            workout,
            (0, 1000, 300, 0, 300),
            (1, 1000, 300, 300, 600));
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        await _db.Entry(workout).ReloadAsync();
        workout.RawFitData.Should().NotContain(DeviceLapBackfillService.LapsBackfillMarkerPrefix);
        (await DeviceLapCountAsync(workout.Id)).Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_SkipsGpxOnlyWorkouts()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, startedAt: FixtureStart);
        workout.RawGpxData = """{"trackPoints":[]}""";
        workout.RawFileData = [1, 2, 3];
        workout.RawFileName = "run.gpx";
        workout.RawFileType = "gpx";
        workout.RawFitData = null;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        (await DeviceLapCountAsync(workout.Id)).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_LogsCorruptFile_AndDoesNotThrow()
    {
        var good = await SeedFitCandidateAsync(durationS: 1800, name: "Good");
        good.RawFitData = BuildRawFitWithLaps(
            sessionElapsed: 1800,
            laps:
            [
                LapJson(FixtureStart, distance: 1000, timer: 300, elapsed: 300),
                LapJson(FixtureStart.AddSeconds(300), distance: 1000, timer: 300, elapsed: 300)
            ]);

        var bad = await SeedFitCandidateAsync(durationS: 1200, name: "Corrupt");
        bad.RawFileData = [0x00, 0x01, 0x02, 0x03];
        bad.RawFileName = "bad.fit";
        bad.RawFileType = "fit";
        // No laps in JSON → forces reparse of corrupt bytes
        bad.RawFitData = """{"session":{"totalElapsedTime":1200},"source":"fit_import"}""";
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        _logger.Messages.Should().Contain(m => m.Contains("Device lap backfill failed for workout"));

        await _db.Entry(good).ReloadAsync();
        Marker(good.RawFitData!).Should().Be(DeviceLapBackfillService.MarkerApplied);

        await _db.Entry(bad).ReloadAsync();
        bad.RawFitData.Should().NotContain(DeviceLapBackfillService.LapsBackfillMarkerPrefix);
    }

    private async Task<Workout> SeedFitCandidateAsync(
        int durationS,
        double distanceM = 5000,
        string name = "Device lap backfill candidate")
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: distanceM,
            durationS: durationS,
            name: name);
        workout.RawFileType = "fit";
        workout.RawFileName = "candidate.fit";
        return workout;
    }

    private async Task<int> DeviceLapCountAsync(Guid workoutId) =>
        await _db.WorkoutSplits.CountAsync(
            s => s.WorkoutId == workoutId && s.Kind == WorkoutSplitKinds.DeviceLap);

    private static string? Marker(string rawFitData)
    {
        using var doc = JsonDocument.Parse(rawFitData);
        return doc.RootElement.TryGetProperty(DeviceLapBackfillService.LapsBackfillKey, out var el)
            ? el.GetString()
            : null;
    }

    private static object LapJson(
        System.DateTime start,
        double distance,
        double? timer,
        double elapsed,
        byte? hr = null) => new
    {
        start = start.ToString("O"),
        timestamp = start.AddSeconds(elapsed).ToString("O"),
        distance,
        timer,
        elapsed,
        avgHeartRate = hr,
        trigger = "distance"
    };

    private static string BuildRawFitWithLaps(int sessionElapsed, object[] laps)
    {
        var payload = new
        {
            session = new { totalElapsedTime = sessionElapsed },
            laps,
            source = "fit_import"
        };
        return JsonSerializer.Serialize(payload);
    }

    private sealed record SyntheticLap(
        System.DateTime Start,
        float ElapsedSeconds,
        float TimerSeconds,
        float DistanceMeters,
        byte? AvgHeartRate,
        LapTrigger Trigger);

    private static byte[] CreateFitWithLaps(
        float elapsedSeconds,
        float timerSeconds,
        IReadOnlyList<SyntheticLap> laps)
    {
        var start = FixtureStart;
        var fitStart = new FitDateTime(start);

        using var stream = new MemoryStream();
        var encode = new Encode(stream, ProtocolVersion.V20);

        var fileId = new FileIdMesg();
        fileId.SetType(FitFile.Activity);
        fileId.SetTimeCreated(fitStart);
        encode.Write(fileId);

        for (var i = 0; i < 3; i++)
        {
            var record = new RecordMesg();
            record.SetTimestamp(new FitDateTime(start.AddMinutes(i * 5)));
            record.SetDistance(i * 1000f);
            encode.Write(record);
        }

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

        var session = new SessionMesg();
        session.SetStartTime(fitStart);
        session.SetTimestamp(new FitDateTime(start.AddSeconds(elapsedSeconds)));
        session.SetTotalElapsedTime(elapsedSeconds);
        session.SetTotalTimerTime(timerSeconds);
        session.SetTotalDistance(2800f);
        session.SetSport(Sport.Running);
        encode.Write(session);
        encode.Close();

        return stream.ToArray();
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
