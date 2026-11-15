namespace Tempo.Api.Services;

public enum StartupBackfillMode
{
    Run,
    Skip,
}

/// <summary>
/// <c>Tempo:Backfills:OnStartup</c>: whether the upgrade backfill workers run when the API starts.
/// Host config only (restart to change). <c>skip</c> is for a library that is already current or
/// for debugging a hang; it is not the deprecation plan (#218).
/// </summary>
public static class StartupBackfills
{
    public const string ConfigKey = "Tempo:Backfills:OnStartup";

    public const string RunName = "run";
    public const string SkipName = "skip";

    /// <summary>Log names, in registration order. Each worker logs "{Name} backfill: N of N".</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "workout route preview",
        "split heart rate",
        "FIT cadence",
        "timer time",
        "device lap",
        "zone seconds",
        "best effort",
    ];

    /// <summary>
    /// Null, empty, or whitespace → <see cref="StartupBackfillMode.Run"/>. Case-insensitive.
    /// Unknown values throw before the host listens.
    /// </summary>
    public static StartupBackfillMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), RunName, StringComparison.OrdinalIgnoreCase))
        {
            return StartupBackfillMode.Run;
        }

        if (string.Equals(value.Trim(), SkipName, StringComparison.OrdinalIgnoreCase))
        {
            return StartupBackfillMode.Skip;
        }

        throw new InvalidOperationException(
            $"Unknown {ConfigKey} '{value.Trim()}'. Allowed values: {RunName}, {SkipName}.");
    }

    /// <summary>The one startup line that names the backfill set (or says it is skipped).</summary>
    public static string DescribeStartup(StartupBackfillMode mode) =>
        mode == StartupBackfillMode.Run
            ? $"Startup backfills: run ({string.Join(", ", Names)}). Each logs \"<name> backfill: N of N\"; \"0 of 0\" means already current."
            : $"Startup backfills: skip ({ConfigKey}=skip). Best effort backfill still runs after a Tempo ZIP restore.";

    /// <summary>
    /// Registers the startup backfill workers. <c>skip</c> registers none of the startup-only
    /// workers; the best-effort worker stays so restores still fill best efforts, but skips its
    /// startup pass.
    /// </summary>
    public static IServiceCollection AddStartupBackfills(this IServiceCollection services, StartupBackfillMode mode)
    {
        services.AddSingleton(typeof(StartupBackfillMode), mode);

        if (mode == StartupBackfillMode.Run)
        {
            services.AddHostedService<RoutePreviewBackfillWorker>();
            services.AddHostedService<SplitHeartRateBackfillWorker>();
            services.AddHostedService<CadenceBackfillWorker>();
            services.AddHostedService<TimerTimeBackfillWorker>();
            services.AddHostedService<DeviceLapBackfillWorker>();
            services.AddHostedService<ZoneSecondsBackfillWorker>();
        }

        services.AddHostedService<BestEffortBackfillWorker>();
        return services;
    }
}
