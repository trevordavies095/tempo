using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Runs backfills one at a time and records the ones that finish idle (#218). Shared by the opt-in
/// startup runner, Tempo ZIP restore, and the <c>backfill</c> command.
/// </summary>
public static class BackfillPass
{
    /// <summary>
    /// Runs each backfill; one that did work runs once more, and a run that processes nothing records
    /// it complete. A backfill that throws, or still has work after two runs, is not recorded.
    /// </summary>
    /// <returns>How many of <paramref name="backfills"/> are now recorded complete.</returns>
    public static async Task<int> RunAsync(
        IServiceScopeFactory scopeFactory,
        IReadOnlyList<BackfillDefinition> backfills,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var recorded = 0;
        foreach (var backfill in backfills)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await RunUntilIdleAsync(scopeFactory, backfill, cancellationToken))
                {
                    recorded++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backfill failed: {Name}", backfill.Name);
            }
        }

        if (recorded == backfills.Count)
        {
            logger.LogInformation("Backfills finished: all {Count} recorded complete", backfills.Count);
        }
        else
        {
            logger.LogWarning(
                "Backfills finished: {Recorded} of {Count} recorded complete; {Remaining} still pending",
                recorded, backfills.Count, backfills.Count - recorded);
        }

        return recorded;
    }

    public static async Task<Dictionary<string, int>> LoadCompletedAsync(
        TempoDbContext db,
        CancellationToken cancellationToken = default) =>
        await db.CompletedBackfills.AsNoTracking()
            .ToDictionaryAsync(c => c.Name, c => c.Version, cancellationToken);

    /// <summary>Backfills not recorded complete at their current version.</summary>
    public static IReadOnlyList<BackfillDefinition> Pending(
        IReadOnlyList<BackfillDefinition> backfills,
        IReadOnlyDictionary<string, int> completed) =>
        backfills
            .Where(b => !completed.TryGetValue(b.Name, out var version) || version != b.Version)
            .ToList();

    public static async Task RecordCompletedAsync(
        TempoDbContext db,
        IEnumerable<BackfillDefinition> backfills,
        CancellationToken cancellationToken = default)
    {
        foreach (var backfill in backfills)
        {
            var row = await db.CompletedBackfills.FindAsync([backfill.Name], cancellationToken);
            if (row == null)
            {
                row = new CompletedBackfill { Name = backfill.Name };
                db.CompletedBackfills.Add(row);
            }

            row.Version = backfill.Version;
            row.CompletedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<bool> RunUntilIdleAsync(
        IServiceScopeFactory scopeFactory,
        BackfillDefinition backfill,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var scope = scopeFactory.CreateScope();
            var processed = await backfill.RunAsync(scope.ServiceProvider, cancellationToken);

            // A cancelled run returns early with work left; only a full, idle run counts
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (processed == 0)
            {
                var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
                db.ChangeTracker.Clear();
                await RecordCompletedAsync(db, [backfill], cancellationToken);
                return true;
            }
        }

        return false;
    }
}
