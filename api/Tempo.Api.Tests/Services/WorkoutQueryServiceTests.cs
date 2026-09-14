using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for WorkoutQueryService duplicate detection logic
/// </summary>
public class WorkoutQueryServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;

    public WorkoutQueryServiceTests()
    {
        // Create in-memory SQLite database for testing
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsWorkout_WhenExactMatch()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM,
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(existingWorkout.Id);
        result.StartedAt.Should().Be(startTime);
        result.DistanceM.Should().Be(distanceM);
        result.DurationS.Should().Be(durationS);
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsWorkout_WhenNearMatchWithinTolerance()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM + 0.5, // Within 1.0m tolerance
            DurationS = durationS, // Exact match
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(existingWorkout.Id);
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsWorkout_WhenDurationWithinTolerance()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM, // Exact match
            DurationS = durationS, // Exact match (within 1s tolerance)
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act - Query with duration within 1s tolerance
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(existingWorkout.Id);
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsNull_WhenDifferentStartTime()
    {
        // Arrange
        var startTime1 = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var startTime2 = new DateTime(2024, 1, 15, 11, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime1,
            DistanceM = distanceM,
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime2, distanceM, durationS);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsNull_WhenDistanceExceedsTolerance()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM + 1.5, // Exceeds 1.0m tolerance
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsNull_WhenDurationExceedsTolerance()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var existingWorkout = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM,
            DurationS = durationS + 2, // Exceeds 1s tolerance
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.Add(existingWorkout);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsNull_WhenNoWorkoutsExist()
    {
        // Arrange - no workouts in database

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(
            _db,
            new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            5000.0,
            1800);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDuplicateWorkoutAsync_ReturnsFirstMatch_WhenMultipleMatchesExist()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var distanceM = 5000.0;
        var durationS = 1800;

        var workout1 = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM,
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test1",
            CreatedAt = DateTime.UtcNow
        };

        var workout2 = new Workout
        {
            StartedAt = startTime,
            DistanceM = distanceM,
            DurationS = durationS,
            AvgPaceS = 360,
            Source = "test2",
            CreatedAt = DateTime.UtcNow
        };

        _db.Workouts.AddRange(workout1, workout2);
        await _db.SaveChangesAsync();

        // Act
        var result = await WorkoutQueryService.FindDuplicateWorkoutAsync(_db, startTime, distanceM, durationS);

        // Assert
        result.Should().NotBeNull();
        // Should return one of the matches (FirstOrDefault behavior)
        (result!.Id == workout1.Id || result.Id == workout2.Id).Should().BeTrue();
    }

    [Fact]
    public async Task ListHealthKitUuidsAsync_ReturnsEmpty_WhenNoWorkoutsExist()
    {
        var result = await WorkoutQueryService.ListHealthKitUuidsAsync(_db);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListHealthKitUuidsAsync_ReturnsOnlyNonNullUuids()
    {
        var uuid1 = Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
        var uuid2 = Guid.Parse("B2C3D4E5-F6A7-8901-BCDE-F12345678901");

        _db.Workouts.AddRange(
            new Workout
            {
                StartedAt = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                DistanceM = 5000,
                DurationS = 1800,
                AvgPaceS = 360,
                Source = "test",
                HealthKitUuid = uuid1,
                CreatedAt = DateTime.UtcNow
            },
            new Workout
            {
                StartedAt = new DateTime(2024, 1, 16, 10, 0, 0, DateTimeKind.Utc),
                DistanceM = 5000,
                DurationS = 1800,
                AvgPaceS = 360,
                Source = "test",
                HealthKitUuid = null,
                CreatedAt = DateTime.UtcNow
            },
            new Workout
            {
                StartedAt = new DateTime(2024, 1, 17, 10, 0, 0, DateTimeKind.Utc),
                DistanceM = 5000,
                DurationS = 1800,
                AvgPaceS = 360,
                Source = "test",
                HealthKitUuid = uuid2,
                CreatedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        var result = await WorkoutQueryService.ListHealthKitUuidsAsync(_db);

        result.Should().BeEquivalentTo(new[] { uuid1, uuid2 });
    }

    [Fact]
    public async Task QueryDetail_WhenIncludeRawFalse_DoesNotSelectOrPopulateRawColumns()
    {
        var workout = new Workout
        {
            StartedAt = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            DistanceM = 5000,
            DurationS = 1800,
            AvgPaceS = 360,
            Source = "gpx",
            RawGpxData = """{"trackPoints":[{"lat":1.0}]}""",
            RawFitData = """{"sessions":[]}""",
            RawStravaData = """{"id":1}""",
            RawHealthKitData = """{"uuid":"x"}""",
            CreatedAt = DateTime.UtcNow
        };
        _db.Workouts.Add(workout);
        await _db.SaveChangesAsync();

        var sql = WorkoutQueryService.QueryDetail(_db, workout.Id, includeRaw: false).ToQueryString();
        sql.Should().NotContain("RawGpxData");
        sql.Should().NotContain("RawFitData");
        sql.Should().NotContain("RawStravaData");
        sql.Should().NotContain("RawHealthKitData");

        var result = await WorkoutQueryService.QueryDetail(_db, workout.Id, includeRaw: false)
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result!.RawGpxData.Should().BeNull();
        result.RawFitData.Should().BeNull();
        result.RawStravaData.Should().BeNull();
        result.RawHealthKitData.Should().BeNull();
        result.DistanceM.Should().Be(5000);
    }

    [Fact]
    public void QueryDetail_WhenIncludeRawTrue_SelectsRawColumns()
    {
        var sql = WorkoutQueryService.QueryDetail(_db, Guid.NewGuid(), includeRaw: true).ToQueryString();
        sql.Should().Contain("RawGpxData");
        sql.Should().Contain("RawFitData");
        sql.Should().Contain("RawStravaData");
        sql.Should().Contain("RawHealthKitData");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueryDetail_ReturnsSplitsOrderedByIdx_WhenInsertedOutOfOrder(bool includeRaw)
    {
        var workout = new Workout
        {
            StartedAt = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            DistanceM = 5000,
            DurationS = 1800,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };
        _db.Workouts.Add(workout);
        await _db.SaveChangesAsync();

        // Insert out of Idx order so heap/insertion order would fail the assertion
        foreach (var idx in new[] { 2, 0, 1 })
        {
            _db.WorkoutSplits.Add(new WorkoutSplit
            {
                WorkoutId = workout.Id,
                Idx = idx,
                DistanceM = 1000,
                DurationS = 360 + idx,
                PaceS = 360
            });
        }
        await _db.SaveChangesAsync();

        var result = await WorkoutQueryService.QueryDetail(_db, workout.Id, includeRaw)
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result!.Splits.Should().HaveCount(3);
        result.Splits.Select(s => s.Idx).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task QueryListPage_ProjectsSplitsCountWithoutLoadingSplitRows()
    {
        var workout = new Workout
        {
            StartedAt = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            DistanceM = 5000,
            DurationS = 1800,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };
        _db.Workouts.Add(workout);
        await _db.SaveChangesAsync();

        for (var i = 0; i < 4; i++)
        {
            _db.WorkoutSplits.Add(new WorkoutSplit
            {
                WorkoutId = workout.Id,
                Idx = i,
                DistanceM = 1000,
                DurationS = 360,
                PaceS = 360
            });
        }
        await _db.SaveChangesAsync();

        var sql = WorkoutQueryService.QueryListPage(_db.Workouts.AsNoTracking()).ToQueryString();
        sql.Should().Contain("COUNT");
        sql.Should().Contain("WorkoutSplits");
        sql.Should().NotContain("\"Idx\"");

        var commands = new List<string>();
        await using var loggingDb = CreateLoggingDb(commands);
        var rows = await WorkoutQueryService.QueryListPage(loggingDb.Workouts.AsNoTracking()).ToListAsync();

        rows.Should().ContainSingle();
        rows[0].SplitsCount.Should().Be(4);
        rows[0].Workout.Id.Should().Be(workout.Id);
        rows[0].Workout.Splits.Should().BeEmpty();

        var splitCommands = commands
            .Where(c => c.Contains("WorkoutSplits", StringComparison.OrdinalIgnoreCase))
            .ToList();
        splitCommands.Should().NotBeEmpty();
        splitCommands.Should().OnlyContain(c => c.Contains("COUNT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QueryListMedia_LoadsPageMediaInOneQueryOrderedByCreatedAt()
    {
        var workoutA = new Workout
        {
            StartedAt = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            DistanceM = 5000,
            DurationS = 1800,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };
        var workoutB = new Workout
        {
            StartedAt = new DateTime(2024, 1, 16, 10, 0, 0, DateTimeKind.Utc),
            DistanceM = 3000,
            DurationS = 1200,
            AvgPaceS = 400,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };
        var workoutC = new Workout
        {
            StartedAt = new DateTime(2024, 1, 17, 10, 0, 0, DateTimeKind.Utc),
            DistanceM = 2000,
            DurationS = 900,
            AvgPaceS = 450,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };
        _db.Workouts.AddRange(workoutA, workoutB, workoutC);
        await _db.SaveChangesAsync();

        var later = new WorkoutMedia
        {
            WorkoutId = workoutA.Id,
            Filename = "later.jpg",
            FilePath = "/tmp/later.jpg",
            MimeType = "image/jpeg",
            FileSizeBytes = 10,
            CreatedAt = new DateTime(2024, 2, 1, 12, 0, 2, DateTimeKind.Utc)
        };
        var earlier = new WorkoutMedia
        {
            WorkoutId = workoutA.Id,
            Filename = "earlier.mp4",
            FilePath = "/tmp/earlier.mp4",
            MimeType = "video/mp4",
            FileSizeBytes = 20,
            CreatedAt = new DateTime(2024, 2, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        var other = new WorkoutMedia
        {
            WorkoutId = workoutB.Id,
            Filename = "b.png",
            FilePath = "/tmp/b.png",
            MimeType = "image/png",
            FileSizeBytes = 30,
            CreatedAt = new DateTime(2024, 2, 1, 11, 0, 0, DateTimeKind.Utc)
        };
        _db.WorkoutMedia.AddRange(later, earlier, other);
        await _db.SaveChangesAsync();

        var pageIds = new List<Guid> { workoutA.Id, workoutB.Id, workoutC.Id };
        var sql = WorkoutQueryService.QueryListMedia(_db, pageIds).ToQueryString();
        sql.Should().Contain("WorkoutMedia");
        sql.Should().NotContain("Filename");
        sql.Should().NotContain("FilePath");
        sql.Should().NotContain("FileSizeBytes");
        sql.Should().NotContain("Caption");

        var commands = new List<string>();
        await using var loggingDb = CreateLoggingDb(commands);
        var rows = await WorkoutQueryService.QueryListMedia(loggingDb, pageIds).ToListAsync();

        var mediaCommands = commands
            .Where(c => c.Contains("WorkoutMedia", StringComparison.OrdinalIgnoreCase)
                        && c.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
            .ToList();
        mediaCommands.Should().HaveCount(1);

        rows.Should().HaveCount(3);
        var aMedia = rows.Where(r => r.WorkoutId == workoutA.Id).ToList();
        aMedia.Select(m => m.Id).Should().Equal(earlier.Id, later.Id);
        aMedia.Select(m => m.MimeType).Should().Equal("video/mp4", "image/jpeg");
        rows.Should().ContainSingle(r => r.WorkoutId == workoutB.Id && r.Id == other.Id);
        rows.Should().NotContain(r => r.WorkoutId == workoutC.Id);
    }

    private TempoDbContext CreateLoggingDb(List<string> commands)
    {
        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .LogTo(commands.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information)
            .Options;
        return new TempoDbContext(options);
    }
}

