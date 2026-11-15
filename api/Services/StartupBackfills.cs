using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;

namespace Tempo.Api.Services;

public enum StartupBackfillMode
{
    Run,
    Skip,
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
/// <c>Tempo:Backfills:OnStartup</c> and the backfill set. Host config only (restart to change).
/// </summary>
public static class StartupBackfills
{
    public const string ConfigKey = "Tempo:Backfills:OnStartup";

    public const string RunName = "run";
    public const string SkipName = "skip";
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
    ];

    /// <summary>
    /// Null, empty, or whitespace → <see cref="StartupBackfillMode.Run"/>. Case-insensitive.
    /// Unknown values throw before the host listens.
    /// </summary>
    public static StartupBackfillMode Parse(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, RunName, StringComparison.OrdinalIgnoreCase))
        {
            return StartupBackfillMode.Run;
        }

        if (string.Equals(trimmed, SkipName, StringComparison.OrdinalIgnoreCase))
        {
            return StartupBackfillMode.Skip;
        }

        if (string.Equals(trimmed, ForceName, StringComparison.OrdinalIgnoreCase))
        {
            return StartupBackfillMode.Force;
        }

        throw new InvalidOperationException(
            $"Unknown {ConfigKey} '{trimmed}'. Allowed values: {RunName}, {SkipName}, {ForceName}.");
    }

    /// <summary>
    /// Registers the runner and its rerun signal. The runner owns every backfill: a startup pass per
    /// <paramref name="mode"/>, then a full pass whenever a Tempo ZIP restore asks for one.
    /// </summary>
    public static IServiceCollection AddStartupBackfills(this IServiceCollection services, StartupBackfillMode mode)
    {
        services.AddSingleton(typeof(StartupBackfillMode), mode);
        services.AddSingleton<IReadOnlyList<BackfillDefinition>>(All);
        services.AddSingleton<BackfillRerunSignal>();
        services.AddHostedService<BackfillRunner>();
        return services;
    }

    /// <summary>
    /// Forgets completion records so every backfill runs again: on the restore-triggered pass and, if
    /// the process stops before that pass finishes, on the next start.
    /// </summary>
    public static Task ClearCompletedAsync(TempoDbContext db, CancellationToken cancellationToken = default) =>
        db.CompletedBackfills.ExecuteDeleteAsync(cancellationToken);
}
