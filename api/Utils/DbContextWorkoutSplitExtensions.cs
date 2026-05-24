using Microsoft.EntityFrameworkCore;
using Tempo.Api.Models;

namespace Tempo.Api.Utils;

/// <summary>
/// EF Core change-tracker helpers for <see cref="WorkoutSplit"/> entities.
/// </summary>
public static class DbContextWorkoutSplitExtensions
{
    /// <summary>
    /// Discards pending split changes for a workout so a failed save does not leak into later
    /// <see cref="DbContext.SaveChangesAsync"/> calls on the same scoped context.
    /// </summary>
    public static void RevertPendingWorkoutSplitChanges(this DbContext db, Guid workoutId)
    {
        foreach (var entry in db.ChangeTracker.Entries<WorkoutSplit>()
            .Where(e => e.Entity.WorkoutId == workoutId)
            .ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Unchanged;
                    break;
                case EntityState.Modified:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;
            }
        }
    }
}
