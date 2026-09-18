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

public class SplitHeartRateBackfillServiceTests : IAsyncLifetime
{
    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ListLogger<SplitHeartRateBackfillService> _logger = null!;
    private SplitHeartRateBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _logger = new ListLogger<SplitHeartRateBackfillService>();
        _service = new SplitHeartRateBackfillService(_db, new SplitHeartRateService(), _logger);
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
        (await _db.Workouts.Select(w => w.SplitHeartRateBackfill).ToListAsync())
            .Should().OnlyContain(cursor => cursor == null);

        _logger.Messages.Should().Contain($"Split heart rate backfill: {SplitHeartRateBackfillService.BatchSize} of {total}");
        _logger.Messages.Should().Contain($"Split heart rate backfill: {total} of {total}");

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Split heart rate backfill: 0 of 0");
        (await _db.Workouts.Select(w => w.SplitHeartRateBackfill).ToListAsync())
            .Should().OnlyContain(cursor => cursor == null);
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

    [Fact]
    public async Task RunAsync_NoOverlapLeftover_StampsOnce_LeavesSplitBpmsNull_ThenIdle()
    {
        var workout = await SeedNoOverlapCandidateAsync();

        var first = await _service.RunAsync();
        first.Should().Be(1);
        _logger.Messages.Should().Contain("Split heart rate backfill: 1 of 1");

        await _db.Entry(workout).ReloadAsync();
        workout.SplitHeartRateBackfill.Should().Be(SplitHeartRateBackfillService.NoOverlapCursor);
        (await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync())
            .Should().OnlyContain(s => s.AvgHeartRateBpm == null);

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Split heart rate backfill: 0 of 0");

        await _db.Entry(workout).ReloadAsync();
        workout.SplitHeartRateBackfill.Should().Be(SplitHeartRateBackfillService.NoOverlapCursor);
        (await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync())
            .Should().OnlyContain(s => s.AvgHeartRateBpm == null);
    }

    [Fact]
    public async Task RunAsync_AlreadyStampedNoOverlap_DoesNotPersistOrCount()
    {
        var workout = await SeedNoOverlapCandidateAsync();
        workout.SplitHeartRateBackfill = SplitHeartRateBackfillService.NoOverlapCursor;
        await _db.SaveChangesAsync();

        var saves = new CountingSaveChangesInterceptor();
        await using var db = PostgresTestFixture.CreateContext(_cloneConnectionString, saves);
        var logger = new ListLogger<SplitHeartRateBackfillService>();
        var service = new SplitHeartRateBackfillService(db, new SplitHeartRateService(), logger);

        var processed = await service.RunAsync();

        processed.Should().Be(0);
        saves.Count.Should().Be(0);
        logger.Messages.Should().Contain("Split heart rate backfill: 0 of 0");
        await _db.Entry(workout).ReloadAsync();
        workout.SplitHeartRateBackfill.Should().Be(SplitHeartRateBackfillService.NoOverlapCursor);
        (await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync())
            .Should().OnlyContain(s => s.AvgHeartRateBpm == null);
    }

    [Fact]
    public async Task RunAsync_PersistsAndCounts_OnlyWhenBpmChangesOrNoOverlapWritten()
    {
        var fill = await SeedCandidateAsync();
        var leftover = await SeedNoOverlapCandidateAsync();

        var saves = new CountingSaveChangesInterceptor();
        await using var db = PostgresTestFixture.CreateContext(_cloneConnectionString, saves);
        var logger = new ListLogger<SplitHeartRateBackfillService>();
        var service = new SplitHeartRateBackfillService(db, new SplitHeartRateService(), logger);

        var processed = await service.RunAsync();

        processed.Should().Be(2);
        saves.Count.Should().Be(2);

        _db.ChangeTracker.Clear();
        var filled = await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == fill.Id);
        filled.SplitHeartRateBackfill.Should().BeNull();
        (await _db.WorkoutSplits.AsNoTracking().Where(s => s.WorkoutId == fill.Id).ToListAsync())
            .Should().Contain(s => s.AvgHeartRateBpm != null);

        var stamped = await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == leftover.Id);
        stamped.SplitHeartRateBackfill.Should().Be(SplitHeartRateBackfillService.NoOverlapCursor);
        (await _db.WorkoutSplits.AsNoTracking().Where(s => s.WorkoutId == leftover.Id).ToListAsync())
            .Should().OnlyContain(s => s.AvgHeartRateBpm == null);

        logger.Messages.Clear();
        saves.Count = 0;
        var second = await service.RunAsync();
        second.Should().Be(0);
        saves.Count.Should().Be(0);
        logger.Messages.Should().Contain("Split heart rate backfill: 0 of 0");
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

    /// <summary>
    /// HR samples sit before the first split window. Last-split remainder cannot
    /// claim them — IndexForElapsedWindow only matches elapsed &gt;= StartElapsedS.
    /// </summary>
    private async Task<Workout> SeedNoOverlapCandidateAsync()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: DateTime.UtcNow.AddMinutes(-_seedOffset++),
            distanceM: 2000,
            durationS: 600);

        _db.WorkoutSplits.Add(new WorkoutSplit
        {
            WorkoutId = workout.Id,
            Kind = WorkoutSplitKinds.Distance,
            Idx = 0,
            DistanceM = 2000,
            DurationS = 300,
            PaceS = 150,
            StartElapsedS = 300,
            EndElapsedS = 600
        });
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
                DistanceM = 20,
                HeartRateBpm = 150
            },
            new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = 10,
                DistanceM = 80,
                HeartRateBpm = 160
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

    private sealed class CountingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public int Count { get; set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            Count++;
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
