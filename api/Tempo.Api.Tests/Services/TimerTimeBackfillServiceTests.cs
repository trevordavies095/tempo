using System.Text.Json;
using Dynastream.Fit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;
using FitDateTime = Dynastream.Fit.DateTime;
using FitFile = Dynastream.Fit.File;

namespace Tempo.Api.Tests.Services;

public class TimerTimeBackfillServiceTests_CandidateSql
{
    [Fact]
    public void PostgresCandidateSql_UsesJsonbPath_AndDoesNotLikeCompactMarker()
    {
        var sql = TimerTimeBackfillService.PostgresCandidateSql;

        sql.Should().Contain("->>'timerTimeBackfill'");
        sql.Should().NotContain("LIKE");
        sql.Should().NotContain(TimerTimeBackfillService.TimerTimeAbsentMarker);
        sql.Should().NotContain("MovingTimeS");
    }
}

public class TimerTimeBackfillServiceTests : IAsyncLifetime
{
    private static readonly System.DateTime FixtureStart =
        new(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);

    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ThrowingSaveChangesInterceptor _saveInterceptor = null!;
    private ListLogger<TimerTimeBackfillService> _logger = null!;
    private TimerTimeBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _saveInterceptor = new ThrowingSaveChangesInterceptor();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString, _saveInterceptor);
        _logger = new ListLogger<TimerTimeBackfillService>();
        _service = new TimerTimeBackfillService(_db, new FitParserService(), _logger);
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
        _logger.Messages.Should().Contain("Timer time backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_CopiesTimerFromSessionJson_AndRewritesPace()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 5000,
            durationS: 1800,
            name: "JSON timer");
        workout.RawFitData = BuildRawFitData(totalTimerTime: 1500);
        workout.TimerTimeS = null;
        workout.AvgPaceS = 1800 / 5.0;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().Be(1500);
        workout.DurationS.Should().Be(1800);
        workout.AvgPaceS.Should().Be(1500 / 5.0);
    }

    [Fact]
    public async Task RunAsync_ReparsesFitBytes_CopiesTimer_AndPatchesSessionJson()
    {
        var fitBytes = CreateFitWithClocks(elapsedSeconds: 1200f, timerSeconds: 1000f);
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 2400,
            durationS: 1200,
            name: "Reparse timer");
        workout.RawFileData = fitBytes;
        workout.RawFileName = "clocks.fit";
        workout.RawFileType = "fit";
        workout.RawFitData = BuildRawFitData(totalTimerTime: null);
        workout.TimerTimeS = null;
        workout.AvgPaceS = 1200 / 2.4;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().Be(1000);
        workout.AvgPaceS.Should().BeApproximately(1000 / 2.4, 1e-6);

        using var doc = JsonDocument.Parse(workout.RawFitData!);
        doc.RootElement.GetProperty("session").GetProperty("totalTimerTime").GetDouble()
            .Should().BeApproximately(1000, 0.01);
        doc.RootElement.TryGetProperty("timerTimeBackfill", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_StampsAbsent_AndSecondRunIsNoOpForTimerCandidates()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 5000,
            durationS: 1800,
            name: "No timer");
        workout.RawFitData = BuildRawFitData(totalTimerTime: null);
        workout.RawFileData = null;
        workout.TimerTimeS = null;
        await _db.SaveChangesAsync();

        var first = await _service.RunAsync();
        first.Should().Be(1);

        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().BeNull();
        using (var doc = JsonDocument.Parse(workout.RawFitData!))
        {
            doc.RootElement.GetProperty(TimerTimeBackfillService.TimerTimeBackfillKey)
                .GetString()
                .Should()
                .Be(TimerTimeBackfillService.TimerTimeBackfillAbsent);
        }

        var compactLike = await _db.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT w."Id" AS "Value"
                FROM "Workouts" AS w
                WHERE w."RawFitData"::text LIKE '%"timerTimeBackfill":"absent"%'
                """)
            .ToListAsync();
        compactLike.Should().BeEmpty();

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Timer time backfill: 0 of 0");

        await _db.Entry(workout).ReloadAsync();
        using (var secondDoc = JsonDocument.Parse(workout.RawFitData!))
        {
            secondDoc.RootElement.GetProperty(TimerTimeBackfillService.TimerTimeBackfillKey)
                .GetString()
                .Should()
                .Be(TimerTimeBackfillService.TimerTimeBackfillAbsent);
        }
    }

    [Fact]
    public async Task RunAsync_IsNoOp_WhenSpacedTimerTimeBackfillAlreadyPresent()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 5000,
            durationS: 1800,
            name: "Spaced stamp");
        workout.RawFitData = BuildRawFitData(totalTimerTime: null, includeSpacedStamp: true);
        workout.TimerTimeS = null;
        workout.AvgPaceS = 1800 / 5.0;
        await _db.SaveChangesAsync();
        await _db.Entry(workout).ReloadAsync();
        var rawBefore = workout.RawFitData;

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        _logger.Messages.Should().Contain("Timer time backfill: 0 of 0");

        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().BeNull();
        workout.AvgPaceS.Should().Be(1800 / 5.0);
        workout.RawFitData.Should().Be(rawBefore);
        using var doc = JsonDocument.Parse(workout.RawFitData!);
        doc.RootElement.GetProperty(TimerTimeBackfillService.TimerTimeBackfillKey)
            .GetString()
            .Should()
            .Be(TimerTimeBackfillService.TimerTimeBackfillAbsent);
        workout.RawFitData.Should().NotContain(TimerTimeBackfillService.TimerTimeAbsentMarker);
    }

    [Fact]
    public async Task RunAsync_IsNoOp_WhenCompactStampAfterJsonbWrite()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 5000,
            durationS: 1800,
            name: "Compact stamp");
        workout.RawFitData = BuildRawFitData(totalTimerTime: null, includeCompactStamp: true);
        workout.TimerTimeS = null;
        await _db.SaveChangesAsync();

        var candidates = await _db.Database
            .SqlQueryRaw<Guid>(TimerTimeBackfillService.PostgresCandidateSql)
            .ToListAsync();
        candidates.Should().BeEmpty();

        var compactLike = await _db.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT w."Id" AS "Value"
                FROM "Workouts" AS w
                WHERE w."RawFitData"::text LIKE '%"timerTimeBackfill":"absent"%'
                """)
            .ToListAsync();
        compactLike.Should().BeEmpty();

        var processed = await _service.RunAsync();
        processed.Should().Be(0);
        _logger.Messages.Should().Contain("Timer time backfill: 0 of 0");

        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().BeNull();
        using var doc = JsonDocument.Parse(workout.RawFitData!);
        doc.RootElement.GetProperty(TimerTimeBackfillService.TimerTimeBackfillKey)
            .GetString()
            .Should()
            .Be(TimerTimeBackfillService.TimerTimeBackfillAbsent);
    }

    [Fact]
    public async Task RunAsync_ReparsesWithoutTimer_StampsAbsent_SecondRunDoesNotReparse()
    {
        var fitBytes = CreateFitWithoutTimer(elapsedSeconds: 1200f);
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 2400,
            durationS: 1200,
            name: "FIT no timer");
        workout.RawFileData = fitBytes;
        workout.RawFileName = "no-timer.fit";
        workout.RawFileType = "fit";
        workout.RawFitData = BuildRawFitData(totalTimerTime: null);
        workout.TimerTimeS = null;
        await _db.SaveChangesAsync();

        var first = await _service.RunAsync();
        first.Should().Be(1);

        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().BeNull();
        using (var absentDoc = JsonDocument.Parse(workout.RawFitData!))
        {
            absentDoc.RootElement.GetProperty(TimerTimeBackfillService.TimerTimeBackfillKey)
                .GetString()
                .Should()
                .Be(TimerTimeBackfillService.TimerTimeBackfillAbsent);
        }

        var compactLike = await _db.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT w."Id" AS "Value"
                FROM "Workouts" AS w
                WHERE w."RawFitData"::text LIKE '%"timerTimeBackfill":"absent"%'
                """)
            .ToListAsync();
        compactLike.Should().BeEmpty();

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Timer time backfill: 0 of 0");
        _logger.Messages.Should().NotContain(m => m.Contains("Timer time backfill failed for workout"));

        await _db.Entry(workout).ReloadAsync();
        using (var secondDoc = JsonDocument.Parse(workout.RawFitData!))
        {
            secondDoc.RootElement.GetProperty(TimerTimeBackfillService.TimerTimeBackfillKey)
                .GetString()
                .Should()
                .Be(TimerTimeBackfillService.TimerTimeBackfillAbsent);
        }
    }

    [Fact]
    public async Task RunAsync_RewritesMovingOnlyPace_WhenTimerStillNull()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 5000,
            durationS: 1800,
            name: "Moving pace");
        workout.TimerTimeS = null;
        workout.MovingTimeS = 1500;
        workout.AvgPaceS = 1500 / 5.0;
        workout.RawFitData = null;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        _logger.Messages.Should().Contain("Timer time backfill: 0 of 0");
        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().BeNull();
        workout.MovingTimeS.Should().Be(1500);
        workout.AvgPaceS.Should().Be(1500 / 5.0);
        workout.RawFitData.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_SkipsAlreadySetTimerTimeS()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 5000,
            durationS: 1800,
            name: "Already set");
        workout.RawFitData = BuildRawFitData(totalTimerTime: 1500);
        workout.TimerTimeS = 1400;
        workout.AvgPaceS = 1400 / 5.0;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().Be(1400);
        workout.AvgPaceS.Should().Be(1400 / 5.0);
    }

    [Fact]
    public async Task RunAsync_SkipsGpxWorkouts()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 5000,
            durationS: 1800,
            name: "GPX");
        workout.RawGpxData = """{"trackPoints":[]}""";
        workout.RawFileData = [1, 2, 3];
        workout.RawFileName = "run.gpx";
        workout.RawFileType = "gpx";
        workout.RawFitData = null;
        workout.TimerTimeS = null;
        workout.AvgPaceS = 360;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().BeNull();
        workout.AvgPaceS.Should().Be(360);
    }

    [Fact]
    public async Task RunAsync_LogsCorruptFile_AndContinuesWithOtherCandidates()
    {
        var good = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 5000,
            durationS: 1800,
            name: "Good JSON timer");
        good.RawFitData = BuildRawFitData(totalTimerTime: 1500);
        good.TimerTimeS = null;
        good.AvgPaceS = 360;
        await _db.SaveChangesAsync();

        var bad = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart.AddDays(1),
            distanceM: 2400,
            durationS: 1200,
            name: "Corrupt FIT");
        bad.RawFileData = [0x00, 0x01, 0x02, 0x03];
        bad.RawFileName = "bad.fit";
        bad.RawFileType = "fit";
        bad.RawFitData = BuildRawFitData(totalTimerTime: null);
        bad.TimerTimeS = null;
        var badAvgPace = bad.AvgPaceS;
        var badDuration = bad.DurationS;
        var badDistance = bad.DistanceM;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(2);
        _logger.Messages.Should().Contain(m => m.Contains("Timer time backfill failed for workout"));

        await _db.Entry(good).ReloadAsync();
        good.TimerTimeS.Should().Be(1500);
        good.AvgPaceS.Should().Be(1500 / 5.0);

        await _db.Entry(bad).ReloadAsync();
        bad.TimerTimeS.Should().BeNull();
        bad.DurationS.Should().Be(badDuration);
        bad.DistanceM.Should().Be(badDistance);
        bad.AvgPaceS.Should().Be(badAvgPace);
        AssertUnparseableStamp(bad.RawFitData);

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Timer time backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_DoesNotStamp_WhenSaveFails()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 5000,
            durationS: 1800,
            name: "Save failure");
        workout.RawFitData = BuildRawFitData(totalTimerTime: null);
        workout.RawFileData = null;
        workout.TimerTimeS = null;
        await _db.SaveChangesAsync();
        await _db.Entry(workout).ReloadAsync();
        var rawBefore = workout.RawFitData;

        _saveInterceptor.Throw = true;
        var processed = await _service.RunAsync();
        _saveInterceptor.Throw = false;

        processed.Should().Be(0);
        await _db.Entry(workout).ReloadAsync();
        workout.RawFitData.Should().Be(rawBefore);
        workout.TimerTimeS.Should().BeNull();
        workout.RawFitData.Should().NotContain(TimerTimeBackfillService.TimerTimeBackfillKey);

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(1);

        await _db.Entry(workout).ReloadAsync();
        workout.TimerTimeS.Should().BeNull();
        using var doc = JsonDocument.Parse(workout.RawFitData!);
        doc.RootElement.GetProperty(TimerTimeBackfillService.TimerTimeBackfillKey)
            .GetString()
            .Should()
            .Be(TimerTimeBackfillService.TimerTimeBackfillAbsent);
    }

    private static string BuildRawFitData(
        double? totalTimerTime,
        bool includeSpacedStamp = false,
        bool includeCompactStamp = false)
    {
        var stampLine = includeCompactStamp
            ? """
                  "timerTimeBackfill":"absent",
              """
            : includeSpacedStamp
                ? """
                      "timerTimeBackfill": "absent",
                  """
                : "";

        if (totalTimerTime.HasValue)
        {
            return $$"""
                {
                {{stampLine}}  "session": {
                    "totalElapsedTime": 1800,
                    "totalTimerTime": {{totalTimerTime.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                    "totalDistance": 5000
                  },
                  "source": "fit_import"
                }
                """;
        }

        return $$"""
            {
            {{stampLine}}  "session": {
                "totalElapsedTime": 1800,
                "totalDistance": 5000
              },
              "source": "fit_import"
            }
            """;
    }

    private static void AssertUnparseableStamp(string? rawFitData)
    {
        rawFitData.Should().NotBeNull();
        using var doc = JsonDocument.Parse(rawFitData!);
        doc.RootElement.GetProperty(TimerTimeBackfillService.TimerTimeBackfillKey)
            .GetString()
            .Should()
            .Be(TimerTimeBackfillService.TimerTimeBackfillUnparseable);
        doc.RootElement.TryGetProperty("session", out _).Should().BeTrue();
        rawFitData.Should().NotContain(TimerTimeBackfillService.TimerTimeAbsentMarker);
    }

    private static byte[] CreateFitWithClocks(float elapsedSeconds, float timerSeconds)
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
            record.SetTimestamp(new FitDateTime(start.AddMinutes(i * 10)));
            record.SetDistance(i * 1200f);
            encode.Write(record);
        }

        var session = new SessionMesg();
        session.SetStartTime(fitStart);
        session.SetTimestamp(new FitDateTime(start.AddMinutes(20)));
        session.SetTotalElapsedTime(elapsedSeconds);
        session.SetTotalTimerTime(timerSeconds);
        session.SetTotalDistance(2400f);
        session.SetSport(Sport.Running);
        encode.Write(session);
        encode.Close();

        return stream.ToArray();
    }

    /// <summary>
    /// FIT session with elapsed only — no TotalTimerTime field written.
    /// </summary>
    private static byte[] CreateFitWithoutTimer(float elapsedSeconds)
    {
        var start = FixtureStart;
        var fitStart = new FitDateTime(start);

        using var stream = new MemoryStream();
        var encode = new Encode(stream, ProtocolVersion.V20);

        var fileId = new FileIdMesg();
        fileId.SetType(FitFile.Activity);
        fileId.SetTimeCreated(fitStart);
        encode.Write(fileId);

        var record = new RecordMesg();
        record.SetTimestamp(fitStart);
        record.SetDistance(0f);
        encode.Write(record);

        var session = new SessionMesg();
        session.SetStartTime(fitStart);
        session.SetTimestamp(new FitDateTime(start.AddMinutes(20)));
        session.SetTotalElapsedTime(elapsedSeconds);
        session.SetTotalDistance(2400f);
        session.SetSport(Sport.Running);
        encode.Write(session);
        encode.Close();

        return stream.ToArray();
    }

    private sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public bool Throw { get; set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            if (Throw)
            {
                throw new InvalidOperationException("simulated save failure");
            }

            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("simulated save failure");
            }

            return ValueTask.FromResult(result);
        }
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
            public void Dispose()
            {
            }
        }
    }
}
