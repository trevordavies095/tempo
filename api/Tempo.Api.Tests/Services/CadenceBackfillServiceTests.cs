using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CadenceBackfillCandidateSqlTests
{
    [Fact]
    public void PostgresCandidateSql_UsesJsonbPath_AndDoesNotLikeCompactMarker()
    {
        var sql = CadenceBackfillService.PostgresCandidateSql;

        sql.Should().Contain("->>'cadenceUnit'");
        sql.Should().Contain("->>'cadenceBackfill'");
        sql.Should().NotContain("LIKE");
        sql.Should().NotContain(CadenceBackfillService.CadenceUnitSpmMarker);
    }
}

public class CadenceBackfillServiceTests : IAsyncLifetime
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "running-cadence-80.fit");

    private static readonly DateTime FixtureStart =
        new(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ThrowingSaveChangesInterceptor _saveInterceptor = null!;
    private ListLogger<CadenceBackfillService> _logger = null!;
    private CadenceBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _saveInterceptor = new ThrowingSaveChangesInterceptor();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString, _saveInterceptor);
        _logger = new ListLogger<CadenceBackfillService>();
        _service = new CadenceBackfillService(_db, new FitParserService(), _logger);
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
        _logger.Messages.Should().Contain("FIT cadence backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_RewritesCadence_AndIsIdempotent()
    {
        var workout = await SeedUnmarkedFitCandidateAsync();

        var first = await _service.RunAsync();
        first.Should().Be(1);

        await _db.Entry(workout).ReloadAsync();
        workout.AvgCadenceRpm.Should().Be(160);
        workout.MaxCadenceRpm.Should().Be(176);
        AssertHasCadenceUnitSpm(workout.RawFitData);

        using (var doc = JsonDocument.Parse(workout.RawFitData!))
        {
            doc.RootElement.GetProperty("cadenceUnit").GetString().Should().Be("spm");
            var session = doc.RootElement.GetProperty("session");
            session.GetProperty("avgCadence").GetInt32().Should().Be(160);
            session.GetProperty("maxCadence").GetInt32().Should().Be(176);
            foreach (var point in doc.RootElement.GetProperty("trackPoints").EnumerateArray())
            {
                point.GetProperty("cad").GetInt32().Should().Be(160);
            }
        }

        var series = await _db.WorkoutTimeSeries
            .Where(ts => ts.WorkoutId == workout.Id)
            .OrderBy(ts => ts.ElapsedSeconds)
            .ToListAsync();
        series.Where(ts => ts.ElapsedSeconds is 0 or 600 or 1200)
            .Should().OnlyContain(ts => ts.CadenceRpm == 160);

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("FIT cadence backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_IsNoOp_WhenSpacedCadenceUnitAlreadyPresent()
    {
        var workout = await SeedUnmarkedFitCandidateAsync();
        workout.RawFitData = BuildLegacyRawFitData(includeSpacedCadenceUnit: true);
        await _db.SaveChangesAsync();
        await _db.Entry(workout).ReloadAsync();
        var rawBefore = workout.RawFitData;

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        _logger.Messages.Should().Contain("FIT cadence backfill: 0 of 0");

        await _db.Entry(workout).ReloadAsync();
        workout.AvgCadenceRpm.Should().Be(80);
        workout.MaxCadenceRpm.Should().Be(88);
        workout.RawFitData.Should().Be(rawBefore);
        AssertHasCadenceUnitSpm(workout.RawFitData);
        workout.RawFitData.Should().NotContain(CadenceBackfillService.CadenceUnitSpmMarker);
    }

    [Fact]
    public async Task PostgresCandidateSql_SkipsJsonbWrittenCompactCadenceUnit_CompactLikeMisses()
    {
        var workout = await SeedUnmarkedFitCandidateAsync();
        workout.RawFitData = BuildLegacyRawFitData(includeCompactCadenceUnit: true);
        await _db.SaveChangesAsync();

        var candidates = await _db.Database
            .SqlQueryRaw<Guid>(CadenceBackfillService.PostgresCandidateSql)
            .ToListAsync();
        candidates.Should().BeEmpty();

        var compactLike = await _db.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT w."Id" AS "Value"
                FROM "Workouts" AS w
                WHERE w."RawFitData"::text LIKE '%"cadenceUnit":"spm"%'
                """)
            .ToListAsync();
        compactLike.Should().BeEmpty();

        var brokenCandidate = await _db.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT w."Id" AS "Value"
                FROM "Workouts" AS w
                WHERE w."RawFileData" IS NOT NULL
                  AND w."RawFitData" IS NOT NULL
                  AND w."RawFitData"::text <> ''
                  AND w."RawFitData"::text NOT LIKE '%"cadenceUnit":"spm"%'
                """)
            .ToListAsync();
        brokenCandidate.Should().Equal(workout.Id);
    }

    [Fact]
    public async Task RunAsync_SkipsGpxOnlyWorkouts()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, startedAt: FixtureStart);
        workout.RawGpxData = """{"trackPoints":[{"cad":170}]}""";
        workout.RawFileData = [1, 2, 3];
        workout.RawFileName = "run.gpx";
        workout.RawFileType = "gpx";
        workout.AvgCadenceRpm = 170;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        await _db.Entry(workout).ReloadAsync();
        workout.AvgCadenceRpm.Should().Be(170);
    }

    [Fact]
    public async Task RunAsync_SkipsJsonOnlyFitWithoutFileBytes()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, startedAt: FixtureStart);
        workout.RawFitData = BuildLegacyRawFitData();
        workout.RawFileData = null;
        workout.AvgCadenceRpm = 80;
        workout.MaxCadenceRpm = 88;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        await _db.Entry(workout).ReloadAsync();
        workout.AvgCadenceRpm.Should().Be(80);
        workout.RawFitData.Should().NotContain(CadenceBackfillService.CadenceUnitSpmMarker);
    }

    [Fact]
    public async Task RunAsync_LeavesUnmatchedElapsedSeriesUnchanged()
    {
        var workout = await SeedUnmarkedFitCandidateAsync(includeUnmatchedSeries: true);

        await _service.RunAsync();

        var unmatched = await _db.WorkoutTimeSeries
            .SingleAsync(ts => ts.WorkoutId == workout.Id && ts.ElapsedSeconds == 999);
        unmatched.CadenceRpm.Should().Be(80);
    }

    [Fact]
    public async Task RunAsync_LogsCorruptFile_AndContinuesWithOtherCandidates()
    {
        var good = await SeedUnmarkedFitCandidateAsync();

        var bad = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart.AddDays(1),
            name: "Corrupt FIT");
        bad.RawFileData = [0x00, 0x01, 0x02, 0x03];
        bad.RawFileName = "bad.fit";
        bad.RawFileType = "fit";
        bad.RawFitData = BuildLegacyRawFitData();
        bad.AvgCadenceRpm = 80;
        bad.MaxCadenceRpm = 88;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(2);
        _logger.Messages.Should().Contain(m => m.Contains("FIT cadence backfill failed for workout"));

        await _db.Entry(good).ReloadAsync();
        good.AvgCadenceRpm.Should().Be(160);
        AssertHasCadenceUnitSpm(good.RawFitData);

        await _db.Entry(bad).ReloadAsync();
        bad.AvgCadenceRpm.Should().Be(80);
        bad.MaxCadenceRpm.Should().Be(88);
        AssertUnparseableStamp(bad.RawFitData);

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("FIT cadence backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_DoesNotStamp_WhenSaveFails()
    {
        var workout = await SeedUnmarkedFitCandidateAsync();
        await _db.Entry(workout).ReloadAsync();
        var rawBefore = workout.RawFitData;

        _saveInterceptor.Throw = true;
        var processed = await _service.RunAsync();
        _saveInterceptor.Throw = false;

        processed.Should().Be(0);
        await _db.Entry(workout).ReloadAsync();
        workout.RawFitData.Should().Be(rawBefore);
        workout.RawFitData.Should().NotContain(CadenceBackfillService.CadenceBackfillKey);
        workout.RawFitData.Should().NotContain(CadenceBackfillService.CadenceUnitSpmMarker);
        workout.AvgCadenceRpm.Should().Be(80);

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(1);

        await _db.Entry(workout).ReloadAsync();
        workout.AvgCadenceRpm.Should().Be(160);
        AssertHasCadenceUnitSpm(workout.RawFitData);
    }

    private async Task<Workout> SeedUnmarkedFitCandidateAsync(bool includeUnmatchedSeries = false)
    {
        File.Exists(FixturePath).Should().BeTrue("running-cadence-80.fit must be in test output");

        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: FixtureStart,
            distanceM: 2400,
            durationS: 1200,
            name: "Cadence backfill candidate");

        workout.RawFileData = await File.ReadAllBytesAsync(FixturePath);
        workout.RawFileName = "running-cadence-80.fit";
        workout.RawFileType = "fit";
        workout.RawFitData = BuildLegacyRawFitData();
        workout.AvgCadenceRpm = 80;
        workout.MaxCadenceRpm = 88;

        foreach (var elapsed in new[] { 0, 600, 1200 })
        {
            _db.WorkoutTimeSeries.Add(new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = elapsed,
                DistanceM = elapsed == 0 ? 0 : elapsed,
                CadenceRpm = 80,
                HeartRateBpm = 140
            });
        }

        if (includeUnmatchedSeries)
        {
            _db.WorkoutTimeSeries.Add(new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = 999,
                CadenceRpm = 80,
                HeartRateBpm = 140
            });
        }

        await _db.SaveChangesAsync();
        return workout;
    }

    private static string BuildLegacyRawFitData(
        bool includeSpacedCadenceUnit = false,
        bool includeCompactCadenceUnit = false)
    {
        var cadenceUnitLine = includeCompactCadenceUnit
            ? """
                  "cadenceUnit":"spm",
              """
            : includeSpacedCadenceUnit
                ? """
                      "cadenceUnit": "spm",
                  """
                : "";

        return $$"""
            {
            {{cadenceUnitLine}}  "session": {
                "avgCadence": 80,
                "maxCadence": 88,
                "maxRunningCadence": 88,
                "totalDistance": 2400,
                "totalElapsedTime": 1200
              },
              "trackPoints": [
                {
                  "lat": 37.7749,
                  "lon": -122.4194,
                  "ele": 10,
                  "time": "2024-01-15T10:00:00.0000000Z",
                  "cad": 80
                },
                {
                  "lat": 37.7849,
                  "lon": -122.4094,
                  "ele": 20,
                  "time": "2024-01-15T10:10:00.0000000Z",
                  "cad": 80
                },
                {
                  "lat": 37.7949,
                  "lon": -122.3994,
                  "ele": 30,
                  "time": "2024-01-15T10:20:00.0000000Z",
                  "cad": 80
                }
              ],
              "recordCount": 3,
              "hasTimeSeries": true,
              "source": "fit_import"
            }
            """;
    }

    private static void AssertHasCadenceUnitSpm(string? rawFitData)
    {
        rawFitData.Should().NotBeNull();
        using var doc = JsonDocument.Parse(rawFitData!);
        doc.RootElement.GetProperty("cadenceUnit").GetString().Should().Be("spm");
    }

    private static void AssertUnparseableStamp(string? rawFitData)
    {
        rawFitData.Should().NotBeNull();
        using var doc = JsonDocument.Parse(rawFitData!);
        doc.RootElement.GetProperty(CadenceBackfillService.CadenceBackfillKey)
            .GetString()
            .Should()
            .Be(CadenceBackfillService.CadenceBackfillUnparseable);
        doc.RootElement.TryGetProperty("cadenceUnit", out _).Should().BeFalse();
        rawFitData.Should().NotContain(CadenceBackfillService.CadenceUnitSpmMarker);
        rawFitData.Should().NotContain(CadenceBackfillService.CadenceUnitSpmMarkerSpaced);
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
