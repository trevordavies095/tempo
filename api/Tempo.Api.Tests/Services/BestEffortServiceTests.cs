using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for BestEffortService covering calculation logic and error handling
/// </summary>
public class BestEffortServiceTests : IAsyncLifetime
{
    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private BestEffortService _service = null!;
    private ILogger<BestEffortService> _logger = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<BestEffortService>();
        _service = new BestEffortService(_logger);
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

    #region GetBestEffortsAsync Tests

    [Fact]
    public async Task GetBestEffortsAsync_WithNoBestEfforts_ReturnsEmptyList()
    {
        // Act
        var result = await _service.GetBestEffortsAsync(_db);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBestEffortsAsync_WithBestEfforts_ReturnsOrderedList()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 10000);
        await SeedWorkoutBestEffortAsync(workout, "5K", 1200);
        await SeedWorkoutBestEffortAsync(workout, "10K", 2400);
        await SeedWorkoutBestEffortAsync(workout, "1K", 240);

        // Act
        var result = await _service.GetBestEffortsAsync(_db);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result[0].DistanceM.Should().Be(1000); // 1K
        result[1].DistanceM.Should().Be(5000); // 5K
        result[2].DistanceM.Should().Be(10000); // 10K
    }

    [Fact]
    public async Task GetBestEffortsAsync_ReturnsFastestPerDistance_TiesGoToEarliestWorkout()
    {
        var earliest = await TestDataSeeder.SeedWorkoutAsync(_db, startedAt: new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));
        var later = await TestDataSeeder.SeedWorkoutAsync(_db, startedAt: new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc));
        var slowest = await TestDataSeeder.SeedWorkoutAsync(_db, startedAt: new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc));
        await SeedWorkoutBestEffortAsync(later, "5K", 1200);
        await SeedWorkoutBestEffortAsync(earliest, "5K", 1200);
        await SeedWorkoutBestEffortAsync(slowest, "5K", 1300);

        var result = await _service.GetBestEffortsAsync(_db);

        var fiveK = result.Should().ContainSingle().Which;
        fiveK.Distance.Should().Be("5K");
        fiveK.TimeS.Should().Be(1200);
        fiveK.WorkoutId.Should().Be(earliest.Id.ToString());
        fiveK.WorkoutDate.Should().Be("2026-03-01");
    }

    [Fact]
    public async Task GetBestEffortsAsync_OlderFasterWorkoutImportedLater_IsTheBest()
    {
        var newer = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            startedAt: new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            distanceM: 5000,
            durationS: 1500);
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, newer);

        var olderFaster = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            startedAt: new DateTime(2023, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            distanceM: 5000,
            durationS: 1200);
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, olderFaster);

        var fiveK = (await _service.GetBestEffortsAsync(_db)).Single(r => r.Distance == "5K");
        fiveK.TimeS.Should().Be(1200);
        fiveK.WorkoutId.Should().Be(olderFaster.Id.ToString());
        fiveK.WorkoutDate.Should().Be("2023-06-01");
    }

    #endregion

    #region CalculateBestEffortForWorkoutAsync Tests

    [Fact]
    public async Task CalculateBestEffortForWorkoutAsync_WithWorkoutTooShort_ReturnsNull()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 1000); // 1K workout
        var targetDistance = 5000; // 5K target

        // Act
        var result = await _service.CalculateBestEffortForWorkoutAsync(_db, workout, "5K", targetDistance);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CalculateBestEffortForWorkoutAsync_WithTimeSeriesData_CalculatesCorrectly()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 10000, // 10K workout
            durationS: 2400, // 40 minutes
            includeTimeSeries: true);

        var targetDistance = 5000; // 5K target

        // Act
        var result = await _service.CalculateBestEffortForWorkoutAsync(_db, workout, "5K", targetDistance);

        // Assert
        result.Should().NotBeNull();
        result!.Distance.Should().Be("5K");
        result.DistanceM.Should().Be(5000);
        result.TimeS.Should().BeGreaterThan(0);
        result.WorkoutId.Should().Be(workout.Id.ToString());
    }


    [Fact]
    public async Task CalculateBestEffortForWorkoutAsync_WithNoTimeSeriesAndNoRoute_ReturnsNull()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 10000);

        var targetDistance = 5000; // 5K target

        // Act
        var result = await _service.CalculateBestEffortForWorkoutAsync(_db, workout, "5K", targetDistance);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CalculateAllBestEffortsAsync Tests

    [Fact]
    public async Task CalculateAllBestEffortsAsync_WithNoWorkouts_ReturnsEmptyList()
    {
        // Act
        var result = await _service.CalculateAllBestEffortsAsync(_db);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        
        // Verify database is empty
        (await _db.WorkoutBestEfforts.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CalculateAllBestEffortsAsync_WithWorkouts_CalculatesAndSavesBestEfforts()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 10000, // 10K workout
            durationS: 2400,
            includeTimeSeries: true);

        // Act
        var result = await _service.CalculateAllBestEffortsAsync(_db);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        
        // Should have best efforts for distances <= 10K
        var distances = result.Select(r => r.DistanceM).ToList();
        distances.Should().OnlyContain(d => d <= 10000);
        
        // Verify saved to database (one workout, so one row per result)
        var dbBestEfforts = await _db.WorkoutBestEfforts.ToListAsync();
        dbBestEfforts.Should().HaveCount(result.Count);
    }

    [Fact]
    public async Task CalculateAllBestEffortsAsync_ClearsExistingBestEfforts()
    {
        // Arrange
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(_db, distanceM: 10000, includeTimeSeries: true);
        
        // Create existing best effort
        await SeedWorkoutBestEffortAsync(workout, "5K", 2000);

        // Act
        var result = await _service.CalculateAllBestEffortsAsync(_db);

        // Assert
        // Should have recalculated, so old best effort should be replaced
        var dbBestEfforts = await _db.WorkoutBestEfforts.ToListAsync();
        dbBestEfforts.Should().NotContain(be => be.TimeS == 2000);
    }

    [Fact]
    public async Task CalculateAllBestEffortsAsync_WithMultipleWorkouts_SelectsFastest()
    {
        // Arrange
        // Create two workouts with different times for same distance
        var workout1 = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1500, // 25 minutes (slower)
            includeTimeSeries: true);
        
        var workout2 = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200, // 20 minutes (faster)
            includeTimeSeries: true);

        // Act
        var result = await _service.CalculateAllBestEffortsAsync(_db);

        // Assert
        var fiveKResult = result.FirstOrDefault(r => r.Distance == "5K");
        fiveKResult.Should().NotBeNull();
        fiveKResult!.WorkoutId.Should().Be(workout2.Id.ToString()); // Should use faster workout
    }

    #endregion

    #region UpdateBestEffortsForNewWorkoutAsync Tests

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_WithNewFasterTime_UpdatesBestEffort()
    {
        // Arrange
        var existingWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1500,
            includeTimeSeries: true);
        
        // Calculate initial best efforts
        await _service.CalculateAllBestEffortsAsync(_db);
        
        // Create new faster workout
        var newWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200, // Faster
            includeTimeSeries: true);

        // Act
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, newWorkout);

        // Assert
        var bestEffort = (await _service.GetBestEffortsAsync(_db)).FirstOrDefault(be => be.Distance == "5K");
        bestEffort.Should().NotBeNull();
        bestEffort!.WorkoutId.Should().Be(newWorkout.Id.ToString());
        bestEffort.TimeS.Should().BeLessThan(1500);
    }

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_WithSlowerTime_PreservesExisting()
    {
        // Arrange
        var existingWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200, // Fast
            includeTimeSeries: true);
        
        // Calculate initial best efforts
        await _service.CalculateAllBestEffortsAsync(_db);
        
        var existingBestEffort = (await _service.GetBestEffortsAsync(_db)).FirstOrDefault(be => be.Distance == "5K");
        var originalTimeS = existingBestEffort!.TimeS;
        
        // Create new slower workout
        var newWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1500, // Slower
            includeTimeSeries: true);

        // Act
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, newWorkout);

        // Assert
        var bestEffort = (await _service.GetBestEffortsAsync(_db)).FirstOrDefault(be => be.Distance == "5K");
        bestEffort.Should().NotBeNull();
        bestEffort!.TimeS.Should().Be(originalTimeS); // Should preserve faster time
        bestEffort.WorkoutId.Should().Be(existingWorkout.Id.ToString());
    }

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_WithWorkoutTooShort_SkipsDistances()
    {
        // Arrange
        var shortWorkout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 1000, // Only 1K
            includeTimeSeries: true);

        // Act
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, shortWorkout);

        // Assert
        // Should not create best efforts for distances > 1K
        var longDistanceBestEfforts = await _db.WorkoutBestEfforts
            .Where(be => be.DistanceM > 1000)
            .ToListAsync();
        longDistanceBestEfforts.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_WithNonExistentWorkout_LogsWarning()
    {
        // Arrange
        var nonExistentWorkout = new Workout
        {
            Id = Guid.NewGuid(),
            DistanceM = 5000,
            DurationS = 1200,
            StartedAt = DateTime.UtcNow
        };

        // Act
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, nonExistentWorkout);

        // Assert
        // Should not throw, but should log warning
        var bestEfforts = await _db.WorkoutBestEfforts.ToListAsync();
        bestEfforts.Should().BeEmpty();
    }

    #endregion

    #region Workout best efforts

    private static readonly string[] DistancesCoveredBy5K = ["400m", "1/2 mile", "1K", "1 mile", "2 mile", "5K"];

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_StoresOneRowPerCoveredDistance_AndMarksWorkout()
    {
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200,
            includeTimeSeries: true);

        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, workout);

        var rows = await _db.WorkoutBestEfforts.AsNoTracking()
            .Where(wbe => wbe.WorkoutId == workout.Id)
            .ToListAsync();
        rows.Select(r => r.Distance).Should().BeEquivalentTo(DistancesCoveredBy5K);
        rows.Single(r => r.Distance == "5K").TimeS.Should().Be(1200);

        var stored = await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workout.Id);
        stored.BestEffortsComputedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_StoresRows_EvenWhenSlowerThanBest()
    {
        await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200,
            includeTimeSeries: true);
        await _service.CalculateAllBestEffortsAsync(_db);

        var slower = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1500,
            includeTimeSeries: true);

        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, slower);

        var row = await _db.WorkoutBestEfforts.AsNoTracking()
            .SingleAsync(wbe => wbe.WorkoutId == slower.Id && wbe.Distance == "5K");
        row.TimeS.Should().Be(1500);
    }

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_RerunReplacesRows_WithoutDuplicates()
    {
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200,
            includeTimeSeries: true);

        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, workout);
        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, workout);

        var rows = await _db.WorkoutBestEfforts.AsNoTracking()
            .Where(wbe => wbe.WorkoutId == workout.Id)
            .ToListAsync();
        rows.Should().HaveCount(DistancesCoveredBy5K.Length);
    }

    [Fact]
    public async Task UpdateBestEffortsForNewWorkoutAsync_WorkoutShorterThanEveryDistance_StoresNoRows_ButMarks()
    {
        var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 300,
            durationS: 90,
            includeTimeSeries: true);

        await _service.UpdateBestEffortsForNewWorkoutAsync(_db, workout);

        (await _db.WorkoutBestEfforts.AnyAsync(wbe => wbe.WorkoutId == workout.Id)).Should().BeFalse();
        var stored = await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == workout.Id);
        stored.BestEffortsComputedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CalculateAllBestEffortsAsync_RebuildsRowsForEveryWorkout()
    {
        var slower = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1500,
            includeTimeSeries: true);
        var faster = await TestDataSeeder.SeedWorkoutCompleteAsync(
            _db,
            distanceM: 5000,
            durationS: 1200,
            includeTimeSeries: true);
        // A stale row that the rebuild must drop
        _db.WorkoutBestEfforts.Add(new WorkoutBestEffort
        {
            WorkoutId = slower.Id,
            Distance = "10K",
            DistanceM = 10000,
            TimeS = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.CalculateAllBestEffortsAsync(_db);

        var rows = await _db.WorkoutBestEfforts.AsNoTracking().ToListAsync();
        rows.Where(r => r.WorkoutId == slower.Id).Select(r => r.Distance)
            .Should().BeEquivalentTo(DistancesCoveredBy5K);
        rows.Where(r => r.WorkoutId == faster.Id).Select(r => r.Distance)
            .Should().BeEquivalentTo(DistancesCoveredBy5K);
        (await _db.Workouts.AsNoTracking().AllAsync(w => w.BestEffortsComputedAt != null)).Should().BeTrue();

        var fiveK = result.Single(r => r.Distance == "5K");
        fiveK.WorkoutId.Should().Be(faster.Id.ToString());
        fiveK.TimeS.Should().Be(1200);
    }

    #endregion

    #region GetBestEffortPrsAsync Tests

    private static readonly DateOnly Since = new(2026, 10, 26);

    [Fact]
    public async Task GetBestEffortPrsAsync_BeatenAfterSince_ReturnsNewTimeWithPrevious()
    {
        var previous = await SeedDatedEffortAsync(new DateOnly(2026, 9, 1), "5K", 1300);
        var pr = await SeedDatedEffortAsync(new DateOnly(2026, 10, 28), "5K", 1250);

        var result = await _service.GetBestEffortPrsAsync(_db, Since);

        var fiveK = result.Should().ContainSingle().Which;
        fiveK.TimeS.Should().Be(1250);
        fiveK.PreviousTimeS.Should().Be(1300);
        fiveK.WorkoutId.Should().Be(pr.Id.ToString());
        fiveK.WorkoutDate.Should().Be("2026-10-28");
        fiveK.PreviousWorkoutId.Should().Be(previous.Id.ToString());
        fiveK.PreviousWorkoutDate.Should().Be("2026-09-01");
    }

    [Fact]
    public async Task GetBestEffortPrsAsync_PreviousBestTied_UsesEarlierWorkout()
    {
        await SeedDatedEffortAsync(new DateOnly(2026, 9, 1), "5K", 1300);
        var earlier = await SeedDatedEffortAsync(new DateOnly(2026, 8, 1), "5K", 1300);
        await SeedDatedEffortAsync(new DateOnly(2026, 10, 28), "5K", 1250);

        var result = await _service.GetBestEffortPrsAsync(_db, Since);

        var fiveK = result.Should().ContainSingle().Which;
        fiveK.PreviousWorkoutId.Should().Be(earlier.Id.ToString());
        fiveK.PreviousWorkoutDate.Should().Be("2026-08-01");
    }

    [Fact]
    public async Task GetBestEffortPrsAsync_TwoPrsAfterSince_ReturnsLatestWithBestBeforeSince()
    {
        await SeedDatedEffortAsync(new DateOnly(2026, 9, 1), "5K", 1300);
        await SeedDatedEffortAsync(new DateOnly(2026, 10, 27), "5K", 1280);
        await SeedDatedEffortAsync(new DateOnly(2026, 10, 29), "5K", 1260);

        var result = await _service.GetBestEffortPrsAsync(_db, Since);

        var fiveK = result.Should().ContainSingle().Which;
        fiveK.TimeS.Should().Be(1260);
        fiveK.PreviousTimeS.Should().Be(1300);
        fiveK.WorkoutDate.Should().Be("2026-10-29");
    }

    [Theory]
    [InlineData(1350)] // slower
    [InlineData(1300)] // tie is not a PR
    public async Task GetBestEffortPrsAsync_NotFasterThanBefore_ReturnsNothing(int timeAfterSinceS)
    {
        await SeedDatedEffortAsync(new DateOnly(2026, 9, 1), "5K", 1300);
        await SeedDatedEffortAsync(new DateOnly(2026, 10, 28), "5K", timeAfterSinceS);

        var result = await _service.GetBestEffortPrsAsync(_db, Since);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBestEffortPrsAsync_FirstEverEffort_HasNullPrevious()
    {
        await SeedDatedEffortAsync(new DateOnly(2026, 10, 28), "10K", 2700);

        var result = await _service.GetBestEffortPrsAsync(_db, Since);

        var tenK = result.Should().ContainSingle().Which;
        tenK.Distance.Should().Be("10K");
        tenK.PreviousTimeS.Should().BeNull();
        tenK.PreviousWorkoutId.Should().BeNull();
        tenK.PreviousWorkoutDate.Should().BeNull();
    }

    [Fact]
    public async Task GetBestEffortPrsAsync_JudgesByWorkoutDate_NotStoreOrder()
    {
        // Newer, slower effort stored first; older, faster one imported afterwards
        await SeedDatedEffortAsync(new DateOnly(2026, 10, 28), "5K", 1300);
        await SeedDatedEffortAsync(new DateOnly(2024, 5, 1), "5K", 1200);

        var result = await _service.GetBestEffortPrsAsync(_db, Since);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBestEffortPrsAsync_WithFrom_ReturnsSeasonBestThatIsNotAllTime()
    {
        await SeedDatedEffortAsync(new DateOnly(2023, 5, 1), "5K", 1150); // all-time best, before the block
        var blockBest = await SeedDatedEffortAsync(new DateOnly(2026, 9, 1), "5K", 1300); // block best before since
        await SeedDatedEffortAsync(new DateOnly(2026, 10, 28), "5K", 1250);

        var allTime = await _service.GetBestEffortPrsAsync(_db, Since);
        var season = await _service.GetBestEffortPrsAsync(_db, Since, from: new DateOnly(2026, 8, 1));

        allTime.Should().BeEmpty();
        var fiveK = season.Should().ContainSingle().Which;
        fiveK.TimeS.Should().Be(1250);
        fiveK.PreviousTimeS.Should().Be(1300);
        fiveK.PreviousWorkoutId.Should().Be(blockBest.Id.ToString());
        fiveK.PreviousWorkoutDate.Should().Be("2026-09-01");
    }

    [Fact]
    public async Task GetBestEffortPrsAsync_SinceBeforeFrom_FastestInWindowWithNullPrevious()
    {
        await SeedDatedEffortAsync(new DateOnly(2026, 7, 1), "5K", 1150);
        await SeedDatedEffortAsync(new DateOnly(2026, 9, 1), "5K", 1300);
        await SeedDatedEffortAsync(new DateOnly(2026, 9, 15), "5K", 1280);

        var result = await _service.GetBestEffortPrsAsync(
            _db, new DateOnly(2026, 1, 1), from: new DateOnly(2026, 8, 1));

        var fiveK = result.Should().ContainSingle().Which;
        fiveK.TimeS.Should().Be(1280);
        fiveK.PreviousTimeS.Should().BeNull();
        fiveK.PreviousWorkoutId.Should().BeNull();
        fiveK.PreviousWorkoutDate.Should().BeNull();
    }

    [Fact]
    public async Task GetBestEffortPrsAsync_SinceAfterTo_ReturnsNothing()
    {
        await SeedDatedEffortAsync(new DateOnly(2026, 10, 28), "5K", 1250);

        var result = await _service.GetBestEffortPrsAsync(_db, Since, to: new DateOnly(2026, 10, 1));

        result.Should().BeEmpty();
    }

    private async Task<Workout> SeedDatedEffortAsync(DateOnly date, string distance, int timeS)
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc),
            distanceM: 10000);
        await SeedWorkoutBestEffortAsync(workout, distance, timeS);
        return workout;
    }

    #endregion

    #region StandardDistances Tests

    [Fact]
    public void StandardDistances_ContainsExpectedDistances()
    {
        // Act & Assert
        BestEffortService.StandardDistances.Should().ContainKey("400m");
        BestEffortService.StandardDistances.Should().ContainKey("1K");
        BestEffortService.StandardDistances.Should().ContainKey("5K");
        BestEffortService.StandardDistances.Should().ContainKey("10K");
        BestEffortService.StandardDistances.Should().ContainKey("Marathon");
    }

    [Fact]
    public void StandardDistances_HasCorrectValues()
    {
        // Act & Assert
        BestEffortService.StandardDistances["400m"].Should().Be(400);
        BestEffortService.StandardDistances["1K"].Should().Be(1000);
        BestEffortService.StandardDistances["5K"].Should().Be(5000);
        BestEffortService.StandardDistances["10K"].Should().Be(10000);
        BestEffortService.StandardDistances["Marathon"].Should().Be(42195);
    }

    #endregion

    private async Task SeedWorkoutBestEffortAsync(Workout workout, string distance, int timeS)
    {
        _db.WorkoutBestEfforts.Add(new WorkoutBestEffort
        {
            WorkoutId = workout.Id,
            Distance = distance,
            DistanceM = BestEffortService.StandardDistances[distance],
            TimeS = timeS
        });
        await _db.SaveChangesAsync();
    }
}
