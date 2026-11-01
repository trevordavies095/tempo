using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class ZoneSecondsBackfillServiceTests : IAsyncLifetime
{
    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ListLogger<ZoneSecondsBackfillService> _logger = null!;
    private ZoneSecondsBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _logger = new ListLogger<ZoneSecondsBackfillService>();
        _service = new ZoneSecondsBackfillService(
            _db,
            new HeartRateZoneService(),
            new RelativeEffortService(),
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
        _logger.Messages.Should().Contain("Zone seconds backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_FillsZoneSeconds_LeavesRelativeEffort_AndIsIdempotent()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db, age: 30); // Z1 95–114, Z3 133–152, Z5 171–190
        var workout = await SeedWorkoutWithSeriesAsync(110, 110, 140, 140, 180, 180);
        workout.RelativeEffort = 999;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var first = await _service.RunAsync();

        first.Should().Be(1);
        var stored = await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workout.Id);
        // 10 s spacing; last sample counts 1 s.
        new[] { stored.ZoneSecondsZ1, stored.ZoneSecondsZ2, stored.ZoneSecondsZ3, stored.ZoneSecondsZ4, stored.ZoneSecondsZ5 }
            .Should().Equal(20, 0, 20, 0, 11);
        stored.ZoneSecondsComputedAt.Should().NotBeNull();
        stored.RelativeEffort.Should().Be(999);

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Zone seconds backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_MarksWorkoutWithoutHrSeries_WithNullZones()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        workout.AvgHeartRateBpm = 150;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        var stored = await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workout.Id);
        stored.ZoneSecondsComputedAt.Should().NotBeNull();
        stored.ZoneSecondsZ1.Should().BeNull();
        stored.ZoneSecondsZ5.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_LeavesWorkoutsUnmarked_WhenZonesNotConfigured_ThenFillsOnRetry()
    {
        var workout = await SeedWorkoutWithSeriesAsync(140, 140);
        _db.ChangeTracker.Clear();

        var skipped = await _service.RunAsync();

        skipped.Should().Be(0);
        (await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workout.Id))
            .ZoneSecondsComputedAt.Should().BeNull();
        _logger.Messages.Should().Contain(
            "Zone seconds backfill: skipped 1 workouts; heart rate zones are not configured");

        await TestDataSeeder.SeedUserSettingsAsync(_db, age: 30);
        _db.ChangeTracker.Clear();

        var retried = await _service.RunAsync();

        retried.Should().Be(1);
        var stored = await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workout.Id);
        stored.ZoneSecondsZ3.Should().Be(11);
    }

    [Fact]
    public async Task RunAsync_SkipsAlreadyMarkedWorkouts()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db);
        var workout = await SeedWorkoutWithSeriesAsync(140, 140);
        var marked = DateTime.UtcNow.AddDays(-1);
        workout.ZoneSecondsComputedAt = marked;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        var stored = await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workout.Id);
        stored.ZoneSecondsZ3.Should().BeNull();
        stored.ZoneSecondsComputedAt.Should().BeCloseTo(marked, TimeSpan.FromMilliseconds(1));
    }

    private async Task<Workout> SeedWorkoutWithSeriesAsync(params byte[] heartRates)
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, durationS: heartRates.Length * 10);
        for (var i = 0; i < heartRates.Length; i++)
        {
            _db.WorkoutTimeSeries.Add(new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = i * 10,
                HeartRateBpm = heartRates[i]
            });
        }

        await _db.SaveChangesAsync();
        return workout;
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
