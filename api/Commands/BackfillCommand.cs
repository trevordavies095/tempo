using Tempo.Api.Data;
using Tempo.Api.Services;

namespace Tempo.Api.Commands;

public sealed record BackfillArgs(bool Force, bool Help);

/// <summary>
/// Host-only one-shot: <c>dotnet Tempo.Api.dll backfill [--force]</c>. Applies pending migrations,
/// runs every backfill not recorded complete (or all with <c>--force</c>), records the ones that
/// finish idle, and exits. The catch-up path for installs that skipped versions (#218).
/// </summary>
public static class BackfillCommand
{
    public const string Verb = "backfill";

    public const string Usage =
        """
        Usage: dotnet Tempo.Api.dll backfill [--force]

        Fills derived workout data that older Tempo versions didn't store (route previews, timer
        time, device laps, zone seconds, best efforts, ...). Runs each backfill not yet recorded
        complete, records it, and exits. Safe to run while the API is up.

        Options:
          --force   Run every backfill, even ones already recorded complete.
          --help    Show this help.

        Docker:  docker compose exec api dotnet Tempo.Api.dll backfill
        Exit code: 0 when every backfill is recorded complete; 1 otherwise (see the log).
        """;

    public static bool IsVerb(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], Verb, StringComparison.Ordinal);

    public static bool TryParse(IReadOnlyList<string> args, out BackfillArgs parsed, out string? error)
    {
        parsed = new BackfillArgs(false, false);
        error = null;
        if (!IsVerb(args))
        {
            error = $"Expected {Verb} command.";
            return false;
        }

        var force = false;
        var help = false;
        foreach (var arg in args.Skip(1))
        {
            switch (arg)
            {
                case "--force":
                    force = true;
                    break;
                case "--help":
                case "-h":
                    help = true;
                    break;
                default:
                    error = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        parsed = new BackfillArgs(force, help);
        return true;
    }

    public static async Task<int> ExecuteAsync(
        IServiceScopeFactory scopeFactory,
        IReadOnlyList<BackfillDefinition> backfills,
        bool force,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BackfillDefinition> toRun = backfills;
        if (!force)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            toRun = BackfillPass.Pending(backfills, await BackfillPass.LoadCompletedAsync(db, cancellationToken));
            if (toRun.Count == 0)
            {
                logger.LogInformation("Backfills: all {Count} already complete; nothing to run (use --force to rerun)", backfills.Count);
                return 0;
            }
        }

        logger.LogInformation("Backfills: running {Count}: {Names}", toRun.Count, StartupBackfills.Names(toRun));
        var recorded = await BackfillPass.RunAsync(scopeFactory, toRun, logger, cancellationToken);
        return recorded == toRun.Count ? 0 : 1;
    }
}
