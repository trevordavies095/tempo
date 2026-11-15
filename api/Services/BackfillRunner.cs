using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Runs the upgrade backfills one at a time (#218). On start: every backfill not recorded complete
/// at its current version (<c>run</c>), all of them (<c>force</c>), or none (<c>skip</c>). Afterwards,
/// a full pass each time <see cref="BackfillRerunSignal"/> is raised by a Tempo ZIP restore, in every
/// mode. A pass that completes with nothing processed records the backfill complete. Never blocks
/// browsing or <c>/ready</c>.
/// </summary>
public class BackfillRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<BackfillDefinition> _backfills;
    private readonly StartupBackfillMode _mode;
    private readonly BackfillRerunSignal _signal;
    private readonly ILogger<BackfillRunner> _logger;

    public BackfillRunner(
        IServiceScopeFactory scopeFactory,
        IReadOnlyList<BackfillDefinition> backfills,
        StartupBackfillMode mode,
        BackfillRerunSignal signal,
        ILogger<BackfillRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _backfills = backfills;
        _mode = mode;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing here may escape: an unhandled exception in a hosted service stops the whole API.
        try
        {
            await RunStartupPassAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // e.g. database unreachable at boot: nothing is recorded, so the next start retries
            _logger.LogError(ex, "Startup backfills could not start; they run again next start");
        }

        try
        {
            await foreach (var _ in _signal.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation("Backfills: running all {Count} after a Tempo ZIP restore", _backfills.Count);
                try
                {
                    await RunPassAsync(_backfills, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Restore backfill pass failed; records were cleared, so the next start runs them");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunStartupPassAsync(CancellationToken stoppingToken)
    {
        if (_mode == StartupBackfillMode.Skip)
        {
            _logger.LogInformation(
                "Startup backfills: skip ({ConfigKey}=skip). A Tempo ZIP restore still runs them.",
                StartupBackfills.ConfigKey);
            return;
        }

        IReadOnlyList<BackfillDefinition> pending;
        if (_mode == StartupBackfillMode.Force)
        {
            pending = _backfills;
            _logger.LogInformation(
                "Startup backfills: force ({ConfigKey}=force); running all {Count}: {Names}",
                StartupBackfills.ConfigKey, _backfills.Count, Names(_backfills));
        }
        else
        {
            var completed = await LoadCompletedAsync(stoppingToken);
            pending = _backfills
                .Where(b => !completed.TryGetValue(b.Name, out var version) || version != b.Version)
                .ToList();

            if (pending.Count == 0)
            {
                _logger.LogInformation(
                    "Startup backfills: all {Count} complete; none run", _backfills.Count);
                return;
            }

            _logger.LogInformation(
                "Startup backfills: run {Pending} of {Count}: {Names}. Each logs \"<name> backfill: N of N\"; a backfill is recorded complete once a pass finds nothing to do.",
                pending.Count, _backfills.Count, Names(pending));
        }

        await RunPassAsync(pending, stoppingToken);
    }

    private async Task RunPassAsync(IReadOnlyList<BackfillDefinition> backfills, CancellationToken stoppingToken)
    {
        var recorded = 0;
        foreach (var backfill in backfills)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processed = await backfill.RunAsync(scope.ServiceProvider, stoppingToken);

                // Cancellation mid-pass returns early with work left; only a full, idle pass counts
                if (processed == 0 && !stoppingToken.IsCancellationRequested)
                {
                    await RecordCompletedAsync(scope.ServiceProvider.GetRequiredService<TempoDbContext>(), backfill, stoppingToken);
                    recorded++;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Not recorded: it runs again next start
                _logger.LogError(ex, "Backfill failed: {Name}", backfill.Name);
            }
        }

        if (recorded == backfills.Count)
        {
            _logger.LogInformation("Backfills finished: all {Count} recorded complete", backfills.Count);
        }
        else
        {
            _logger.LogInformation(
                "Backfills finished: {Recorded} of {Count} recorded complete; {Remaining} run again next start",
                recorded, backfills.Count, backfills.Count - recorded);
        }
    }

    private async Task<Dictionary<string, int>> LoadCompletedAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        return await db.CompletedBackfills.AsNoTracking()
            .ToDictionaryAsync(c => c.Name, c => c.Version, cancellationToken);
    }

    private static async Task RecordCompletedAsync(
        TempoDbContext db,
        BackfillDefinition backfill,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var row = await db.CompletedBackfills.FindAsync([backfill.Name], cancellationToken);
        if (row == null)
        {
            row = new CompletedBackfill { Name = backfill.Name };
            db.CompletedBackfills.Add(row);
        }

        row.Version = backfill.Version;
        row.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Names(IEnumerable<BackfillDefinition> backfills) =>
        string.Join(", ", backfills.Select(b => b.Name));
}
