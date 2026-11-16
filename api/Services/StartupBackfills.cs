using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;

namespace Tempo.Api.Services;

public enum StartupBackfillMode
{
    Skip,
    Run,
    Force,
}

/// <summary>
/// One upgrade backfill. <paramref name="Version"/> is bumped when the backfill's logic changes so
/// installs that recorded it complete run it once more. <paramref name="RunAsync"/> returns rows
/// processed; a completed pass that processed none means the backfill is done.
/// </summary>
public sealed record BackfillDefinition(
    string Name,
    int Version,
    Func<IServiceProvider, CancellationToken, Task<int>> RunAsync);

/// <summary>
/// The upgrade backfills and <c>Tempo:Backfills:OnStartup</c> (#217, #218). Default <c>skip</c>:
/// normal starts run none and only check that all are recorded complete. Backfills run from a Tempo
/// ZIP restore, the <c>backfill</c> command, or the opt-in <c>run</c> / <c>force</c> startup modes.
/// </summary>
public static class StartupBackfills
{
    public const string ConfigKey = "Tempo:Backfills:OnStartup";

    public const string SkipName = "skip";
    public const string RunName = "run";
    public const string ForceName = "force";

    /// <summary>Run order. Names match each backfill's "&lt;name&gt; backfill: N of N" log line.</summary>
    public static readonly IReadOnlyList<BackfillDefinition> All =
    [
        new("workout route preview", 1, (sp, ct) => sp.GetRequiredService<RoutePreviewBackfillService>().RunAsync(ct)),
        new("split heart rate", 1, (sp, ct) => sp.GetRequiredService<SplitHeartRateBackfillService>().RunAsync(ct)),
        new("FIT cadence", 1, (sp, ct) => sp.GetRequiredService<CadenceBackfillService>().RunAsync(ct)),
        new("timer time", 1, (sp, ct) => sp.GetRequiredService<TimerTimeBackfillService>().RunAsync(ct)),
        new("device lap", 1, (sp, ct) => sp.GetRequiredService<DeviceLapBackfillService>().RunAsync(ct)),
        new("zone seconds", 1, (sp, ct) => sp.GetRequiredService<ZoneSecondsBackfillService>().RunAsync(ct)),
        new("best effort", 1, (sp, ct) => sp.GetRequiredService<BestEffortBackfillService>().RunAsync(ct)),
        new("device name", 1, (sp, ct) => sp.GetRequiredService<DeviceNameBackfillService>().RunAsync(ct)),
    ];

    public const string CatchUpCommand = "docker compose exec api dotnet Tempo.Api.dll backfill";

    /// <summary>
    /// Null, empty, or whitespace → <see cref="StartupBackfillMode.Skip"/>. Case-insensitive.
    /// Unknown values throw before the host listens.
    /// </summary>
    public static StartupBackfillMode Parse(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, SkipName, StringComparison.OrdinalIgnoreCase))
        {
            return StartupBackfillMode.Skip;
        }

        if (string.Equals(trimmed, RunName, StringComparison.OrdinalIgnoreCase))
        {
            return StartupBackfillMode.Run;
        }

        if (string.Equals(trimmed, ForceName, StringComparison.OrdinalIgnoreCase))
        {
            return StartupBackfillMode.Force;
        }

        throw new InvalidOperationException(
            $"Unknown {ConfigKey} '{trimmed}'. Allowed values: {SkipName}, {RunName}, {ForceName}.");
    }

    /// <summary>
    /// Registers the backfill set (restore and the <c>backfill</c> command use it) and, only for
    /// <c>run</c> / <c>force</c>, the one-shot startup runner.
    /// </summary>
    public static IServiceCollection AddStartupBackfills(this IServiceCollection services, StartupBackfillMode mode)
    {
        services.AddSingleton(typeof(StartupBackfillMode), mode);
        services.AddSingleton<IReadOnlyList<BackfillDefinition>>(All);
        if (mode != StartupBackfillMode.Skip)
        {
            services.AddHostedService<BackfillRunner>();
        }

        return services;
    }

    /// <summary>
    /// Default-start check after migrations (<c>skip</c> mode): one query, runs nothing. A library with
    /// no workouts has nothing to fill, so every backfill is recorded complete. A library with workouts
    /// and pending backfills (a skipped-version upgrade, or a restore cut short) gets a warning naming
    /// the catch-up command.
    /// </summary>
    public static async Task CheckAsync(
        TempoDbContext db,
        IReadOnlyList<BackfillDefinition> backfills,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var pending = BackfillPass.Pending(backfills, await BackfillPass.LoadCompletedAsync(db, cancellationToken));
        if (pending.Count == 0)
        {
            logger.LogInformation("Startup backfills: all {Count} complete; none run", backfills.Count);
            return;
        }

        if (!await db.Workouts.AnyAsync(cancellationToken))
        {
            await BackfillPass.RecordCompletedAsync(db, pending, cancellationToken);
            logger.LogInformation(
                "Startup backfills: no workouts yet, nothing to fill; recorded all {Count} complete", backfills.Count);
            return;
        }

        logger.LogWarning(
            "Startup backfills: {Pending} of {Count} have not completed on this library ({Names}). Older workouts may be missing derived data until you run: {Command} (or set TEMPO_BACKFILLS_ON_STARTUP=run for one start)",
            pending.Count, backfills.Count, Names(pending), CatchUpCommand);
    }

    /// <summary>
    /// Forgets completion records so every backfill counts as pending again. Called by a restore before
    /// it repairs, so a repair cut short is caught by the startup check.
    /// </summary>
    public static Task ClearCompletedAsync(TempoDbContext db, CancellationToken cancellationToken = default) =>
        db.CompletedBackfills.ExecuteDeleteAsync(cancellationToken);

    public static string Names(IEnumerable<BackfillDefinition> backfills) =>
        string.Join(", ", backfills.Select(b => b.Name));
}
