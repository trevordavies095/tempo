namespace Tempo.Api.Services;

/// <summary>
/// Opt-in startup pass (<c>Tempo:Backfills:OnStartup</c> = <c>run</c> or <c>force</c>), registered
/// only in those modes. Runs once in the background, then exits; never blocks browsing or
/// <c>/ready</c>. The default (<c>skip</c>) registers no runner: see
/// <see cref="StartupBackfills.CheckAsync"/>.
/// </summary>
public class BackfillRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<BackfillDefinition> _backfills;
    private readonly StartupBackfillMode _mode;
    private readonly ILogger<BackfillRunner> _logger;

    public BackfillRunner(
        IServiceScopeFactory scopeFactory,
        IReadOnlyList<BackfillDefinition> backfills,
        StartupBackfillMode mode,
        ILogger<BackfillRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _backfills = backfills;
        _mode = mode;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing may escape: an unhandled exception in a hosted service stops the whole API.
        try
        {
            IReadOnlyList<BackfillDefinition> pending;
            if (_mode == StartupBackfillMode.Force)
            {
                pending = _backfills;
                _logger.LogInformation(
                    "Startup backfills: force ({ConfigKey}=force); running all {Count}: {Names}",
                    StartupBackfills.ConfigKey, _backfills.Count, StartupBackfills.Names(_backfills));
            }
            else
            {
                using var scope = _scopeFactory.CreateScope();
                var completed = await BackfillPass.LoadCompletedAsync(
                    scope.ServiceProvider.GetRequiredService<Data.TempoDbContext>(), stoppingToken);
                pending = BackfillPass.Pending(_backfills, completed);

                if (pending.Count == 0)
                {
                    _logger.LogInformation("Startup backfills: all {Count} complete; none run", _backfills.Count);
                    return;
                }

                _logger.LogInformation(
                    "Startup backfills: run {Pending} of {Count}: {Names}",
                    pending.Count, _backfills.Count, StartupBackfills.Names(pending));
            }

            await BackfillPass.RunAsync(_scopeFactory, pending, _logger, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // e.g. database unreachable at boot: nothing is recorded, so it stays pending
            _logger.LogError(ex, "Startup backfills could not run; they remain pending");
        }
    }
}
