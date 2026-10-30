using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class BestEffortBackfillServiceTests : IAsyncLifetime
{
    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ListLogger<BestEffortBackfillService> _logger = null!;
    private BestEffortBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _logger = new ListLogger<BestEffortBackfillService>();
        _service = new BestEffortBackfillService(
            _db,
            new BestEffortService(NullLogger<BestEffortService>.Instance),
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
        _logger.Messages.Should().Contain("Best effort backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_FillsRowsForUnmarkedWorkouts_AndIsIdempotent()
    {
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(_db, distanceM: 5000, durationS: 1200);
        _db.ChangeTracker.Clear();

        var first = await _service.RunAsync();

        first.Should().Be(1);
        var rows = await _db.WorkoutBestEfforts.AsNoTracking()
            .Where(wbe => wbe.WorkoutId == workout.Id)
            .ToListAsync();
        rows.Select(r => r.Distance).Should().Contain(["400m", "1K", "1 mile", "5K"]);
        rows.Single(r => r.Distance == "5K").TimeS.Should().Be(1200);
        (await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workout.Id))
            .BestEffortsComputedAt.Should().NotBeNull();

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Best effort backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_ReplacesExistingRows()
    {
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(_db, distanceM: 5000, durationS: 1200);
        // Seeded row (e.g. copied from an old record) that the backfill must replace
        _db.WorkoutBestEfforts.Add(new WorkoutBestEffort
        {
            WorkoutId = workout.Id,
            Distance = "5K",
            DistanceM = 5000,
            TimeS = 999
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _service.RunAsync();

        var fiveK = await _db.WorkoutBestEfforts.AsNoTracking()
            .Where(wbe => wbe.WorkoutId == workout.Id && wbe.Distance == "5K")
            .ToListAsync();
        fiveK.Should().ContainSingle().Which.TimeS.Should().Be(1200);
    }

    [Fact]
    public async Task RunAsync_SkipsMarkedWorkouts()
    {
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(_db, distanceM: 5000, durationS: 1200);
        workout.BestEffortsComputedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        (await _db.WorkoutBestEfforts.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_MarksWorkoutShorterThanEveryDistance()
    {
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(_db, distanceM: 300, durationS: 90);
        _db.ChangeTracker.Clear();

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        (await _db.WorkoutBestEfforts.AnyAsync()).Should().BeFalse();
        (await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workout.Id))
            .BestEffortsComputedAt.Should().NotBeNull();
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

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
    }
}
