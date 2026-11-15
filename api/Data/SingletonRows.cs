using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tempo.Api.Models;

namespace Tempo.Api.Data;

/// <summary>
/// One-row tables (<see cref="UserSettings"/>, <see cref="IntervalsIcuConnection"/>). Postgres enforces
/// one row with a unique index on a constant (migration EnforceSingletonRows); reads go through
/// <c>SingleRowAsync</c> so they are ordered (no EF First-without-OrderBy warning) and match the
/// migration's keep-newest rule.
/// </summary>
public static class SingletonRows
{
    public const string UserSettingsIndex = "IX_UserSettings_Singleton";
    public const string IntervalsIcuConnectionsIndex = "IX_IntervalsIcuConnections_Singleton";

    public static Task<UserSettings?> SingleRowAsync(
        this IQueryable<UserSettings> query,
        CancellationToken cancellationToken = default) =>
        query.OrderByDescending(s => s.UpdatedAt).ThenBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);

    public static Task<IntervalsIcuConnection?> SingleRowAsync(
        this IQueryable<IntervalsIcuConnection> query,
        CancellationToken cancellationToken = default) =>
        query.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);

    /// <summary>True when a save lost the race to insert a singleton row another request just created.</summary>
    public static bool IsSingletonConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UserSettingsIndex or IntervalsIcuConnectionsIndex,
        };

    /// <summary>
    /// Runs a get-or-create read-modify-save. If its insert loses the race to a concurrent request,
    /// detaches the tracked singleton rows and runs it once more, which then updates the row that now
    /// exists. Other tracked entities are left alone (the context may be shared with an import).
    /// <paramref name="body"/> must do its own singleton read and save.
    /// </summary>
    public static async Task<T> RetryOnSingletonConflictAsync<T>(this TempoDbContext db, Func<Task<T>> body)
    {
        try
        {
            return await body();
        }
        catch (DbUpdateException ex) when (IsSingletonConflict(ex))
        {
            foreach (var entry in db.ChangeTracker.Entries()
                         .Where(e => e.Entity is UserSettings or IntervalsIcuConnection)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            return await body();
        }
    }

    public static Task RetryOnSingletonConflictAsync(this TempoDbContext db, Func<Task> body) =>
        db.RetryOnSingletonConflictAsync(async () =>
        {
            await body();
            return true;
        });
}
