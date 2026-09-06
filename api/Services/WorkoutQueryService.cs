using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Service for common workout database queries.
/// </summary>
public static class WorkoutQueryService
{
    /// <summary>
    /// Finds a workout previously imported from HealthKit by HKWorkout UUID.
    /// </summary>
    public static async Task<Workout?> FindByHealthKitUuidAsync(TempoDbContext db, Guid healthKitUuid)
    {
        return await db.Workouts
            .FirstOrDefaultAsync(w => w.HealthKitUuid == healthKitUuid);
    }

    /// <summary>
    /// Returns all non-null HealthKitUuid values stored on workouts (nulls omitted).
    /// Used by tempo-ios to badge already-imported runs without paging GET /workouts.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> ListHealthKitUuidsAsync(TempoDbContext db)
    {
        return await db.Workouts
            .AsNoTracking()
            .Where(w => w.HealthKitUuid != null)
            .Select(w => w.HealthKitUuid!.Value)
            .ToListAsync();
    }

    /// <summary>
    /// Projects a <c>GET /workouts</c> page with <c>splitsCount</c> as a SQL <c>COUNT</c>.
    /// Does not load <see cref="WorkoutSplit"/> rows.
    /// </summary>
    public static IQueryable<WorkoutListPageRow> QueryListPage(IQueryable<Workout> workouts)
    {
        return workouts.Select(w => new WorkoutListPageRow
        {
            Workout = w,
            SplitsCount = w.Splits.Count()
        });
    }

    /// <summary>
    /// Media metadata for a <c>GET /workouts</c> page: one query for the page's workout ids,
    /// ordered by <see cref="WorkoutMedia.CreatedAt"/> then id.
    /// </summary>
    public static IQueryable<WorkoutListMediaRow> QueryListMedia(
        TempoDbContext db,
        IReadOnlyCollection<Guid> workoutIds)
    {
        return db.WorkoutMedia
            .AsNoTracking()
            .Where(m => workoutIds.Contains(m.WorkoutId))
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m => new WorkoutListMediaRow
            {
                WorkoutId = m.WorkoutId,
                Id = m.Id,
                MimeType = m.MimeType
            });
    }

    /// <summary>
    /// Query used by <c>GET /workouts/{id}</c>. When <paramref name="includeRaw"/> is false, the four
    /// raw JSONB columns are projected out so they are neither read nor deserialized.
    /// </summary>
    public static IQueryable<Workout> QueryDetail(TempoDbContext db, Guid id, bool includeRaw)
    {
        var workouts = db.Workouts.AsNoTracking().Where(w => w.Id == id);

        if (includeRaw)
        {
            return workouts
                .Include(w => w.Route)
                .Include(w => w.Splits.OrderBy(s => s.Idx))
                .Include(w => w.Shoe);
        }

        return workouts.Select(w => new Workout
        {
            Id = w.Id,
            StartedAt = w.StartedAt,
            DurationS = w.DurationS,
            DistanceM = w.DistanceM,
            AvgPaceS = w.AvgPaceS,
            ElevGainM = w.ElevGainM,
            ElevLossM = w.ElevLossM,
            MinElevM = w.MinElevM,
            MaxElevM = w.MaxElevM,
            MaxSpeedMps = w.MaxSpeedMps,
            AvgSpeedMps = w.AvgSpeedMps,
            MovingTimeS = w.MovingTimeS,
            MaxHeartRateBpm = w.MaxHeartRateBpm,
            AvgHeartRateBpm = w.AvgHeartRateBpm,
            MinHeartRateBpm = w.MinHeartRateBpm,
            MaxCadenceRpm = w.MaxCadenceRpm,
            AvgCadenceRpm = w.AvgCadenceRpm,
            MaxPowerWatts = w.MaxPowerWatts,
            AvgPowerWatts = w.AvgPowerWatts,
            Calories = w.Calories,
            RelativeEffort = w.RelativeEffort,
            Rpe = w.Rpe,
            Name = w.Name,
            RunType = w.RunType,
            Notes = w.Notes,
            Source = w.Source,
            Device = w.Device,
            HealthKitUuid = w.HealthKitUuid,
            RawFileData = w.RawFileData,
            RawFileName = w.RawFileName,
            RawFileType = w.RawFileType,
            CreatedAt = w.CreatedAt,
            Weather = w.Weather,
            ShoeId = w.ShoeId,
            Shoe = w.Shoe,
            Route = w.Route,
            Splits = w.Splits.OrderBy(s => s.Idx).ToList()
        });
    }

    /// <summary>
    /// Finds a duplicate workout based on start time, distance, and duration.
    /// A workout is considered a duplicate if it has the same start time and very similar distance and duration.
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="startTime">Start time of the workout</param>
    /// <param name="distanceMeters">Distance in meters</param>
    /// <param name="durationSeconds">Duration in seconds</param>
    /// <returns>The existing workout if found, null otherwise</returns>
    public static async Task<Workout?> FindDuplicateWorkoutAsync(
        TempoDbContext db,
        DateTime startTime,
        double distanceMeters,
        int durationSeconds)
    {
        return await db.Workouts
            .Where(w => w.StartedAt == startTime &&
                        Math.Abs(w.DistanceM - distanceMeters) < 1.0 &&
                        Math.Abs(w.DurationS - durationSeconds) < 1)
            .FirstOrDefaultAsync();
    }
}

/// <summary>
/// One row of a <c>GET /workouts</c> page: workout scalars plus a SQL split count.
/// </summary>
public sealed class WorkoutListPageRow
{
    public Workout Workout { get; set; } = null!;
    public int SplitsCount { get; set; }
}

/// <summary>
/// Minimal media metadata for a <c>GET /workouts</c> list item.
/// </summary>
public sealed class WorkoutListMediaRow
{
    public Guid WorkoutId { get; set; }
    public Guid Id { get; set; }
    public string MimeType { get; set; } = string.Empty;
}

