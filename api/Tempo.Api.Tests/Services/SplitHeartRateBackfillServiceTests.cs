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

public class SplitHeartRateBackfillServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly ListLogger<SplitHeartRateBackfillService> _logger;
    private readonly SplitHeartRateBackfillService _service;

    public SplitHeartRateBackfillServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();
        _logger = new ListLogger<SplitHeartRateBackfillService>();
        _service = new SplitHeartRateBackfillService(_db, new SplitHeartRateService(), _logger);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task RunAsync_IsNoOp_WhenNoCandidates()
    {
        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        _logger.Messages.Should().Contain("Split heart rate backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_FillsAllNullSplits_InBatches_AndIsIdempotent()
    {
        var total = SplitHeartRateBackfillService.BatchSize + 5;
        for (var i = 0; i < total; i++)
        {
            await SeedCandidateAsync();
        }

        var first = await _service.RunAsync();
        first.Should().Be(total);

        var splits = await _db.WorkoutSplits.ToListAsync();
        splits.Should().OnlyContain(s => s.AvgHeartRateBpm != null);

        _logger.Messages.Should().Contain($"Split heart rate backfill: {SplitHeartRateBackfillService.BatchSize} of {total}");
        _logger.Messages.Should().Contain($"Split heart rate backfill: {total} of {total}");

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Split heart rate backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_SkipsWorkout_WhenAnySplitAlreadyFilled()
    {
        var workout = await SeedCandidateAsync();
        var first = await _db.WorkoutSplits.OrderBy(s => s.Idx).FirstAsync(s => s.WorkoutId == workout.Id);
        first.AvgHeartRateBpm = 140;
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        var splits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workout.Id)
            .OrderBy(s => s.Idx)
            .ToListAsync();
        splits[0].AvgHeartRateBpm.Should().Be(140);
        splits.Skip(1).Should().OnlyContain(s => s.AvgHeartRateBpm == null);
    }

    [Fact]
    public async Task RunAsync_SkipsSeriesWithoutHeartRateSamples()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 2000, durationS: 600);
        await TestDataSeeder.SeedWorkoutWithSplitsAsync(_db, workout);
        _db.WorkoutTimeSeries.Add(new WorkoutTimeSeries
        {
            WorkoutId = workout.Id,
            ElapsedSeconds = 10,
            DistanceM = 100,
            HeartRateBpm = null
        });
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        (await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync())
            .Should().OnlyContain(s => s.AvgHeartRateBpm == null);
    }

    [Fact]
    public async Task RunAsync_DoesNotRewriteSplitGeometry()
    {
        var workout = await SeedCandidateAsync();
        var before = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workout.Id)
            .OrderBy(s => s.Idx)
            .Select(s => new { s.Idx, s.DistanceM, s.DurationS, s.PaceS })
            .ToListAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        var after = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workout.Id)
            .OrderBy(s => s.Idx)
            .ToListAsync();
        after.Select(s => new { s.Idx, s.DistanceM, s.DurationS, s.PaceS }).Should().Equal(before);
        after.Should().Contain(s => s.AvgHeartRateBpm != null);
    }

    [Fact]
    public async Task RunAsync_FillsIndoorWorkoutWithoutRoute()
    {
        var workout = await SeedCandidateAsync(withRoute: false);

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        (await _db.WorkoutRoutes.CountAsync(r => r.WorkoutId == workout.Id)).Should().Be(0);
        (await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync())
            .Should().Contain(s => s.AvgHeartRateBpm != null);
    }

    [Fact]
    public async Task RunAsync_FillsLongMileSplits_FromDenseHeartRateSeries()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 29_000, durationS: 12_000);
        var mileSplits = new List<WorkoutSplit>();
        for (var idx = 0; idx < 18; idx++)
        {
            mileSplits.Add(new WorkoutSplit
            {
                WorkoutId = workout.Id,
                Idx = idx,
                DistanceM = 1610,
                DurationS = 650,
                PaceS = 400
            });
        }
        WorkoutSplitElapsed.FillFromCumulativeDuration(mileSplits);
        _db.WorkoutSplits.AddRange(mileSplits);

        var series = new List<WorkoutTimeSeries>(12_000);
        for (var elapsed = 0; elapsed < 12_000; elapsed++)
        {
            series.Add(new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = elapsed,
                DistanceM = elapsed * 2.4,
                HeartRateBpm = (byte)(140 + elapsed % 20)
            });
        }

        _db.WorkoutTimeSeries.AddRange(series);
        await _db.SaveChangesAsync();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        var splits = await _db.WorkoutSplits
            .Where(s => s.WorkoutId == workout.Id)
            .OrderBy(s => s.Idx)
            .ToListAsync();
        splits.Should().OnlyContain(s => s.AvgHeartRateBpm != null);
    }

    private async Task<Workout> SeedCandidateAsync(bool withRoute = false)
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: DateTime.UtcNow.AddMinutes(-_seedOffset++),
            distanceM: 2000,
            durationS: 600);
        if (withRoute)
        {
            await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout);
        }

        await TestDataSeeder.SeedWorkoutWithSplitsAsync(_db, workout);
        _db.WorkoutTimeSeries.AddRange(
            new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = 0,
                DistanceM = 0,
                HeartRateBpm = 140
            },
            new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = 1,
                DistanceM = 500,
                HeartRateBpm = 160
            },
            new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = 300,
                DistanceM = 1100,
                HeartRateBpm = 170
            });
        await _db.SaveChangesAsync();
        return workout;
    }

    private int _seedOffset;

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
