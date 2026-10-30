namespace Tempo.Api.Services;

/// <summary>
/// Hosted service that fills per-workout best efforts after migrations have applied, then again
/// whenever <see cref="BestEffortBackfillSignal"/> is raised (after a Tempo ZIP restore).
/// Does not block browsing; until a workout is filled it is missing from best-effort results.
/// </summary>
public class BestEffortBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BestEffortBackfillSignal _signal;
    private readonly ILogger<BestEffortBackfillWorker> _logger;

    public BestEffortBackfillWorker(
        IServiceScopeFactory scopeFactory,
        BestEffortBackfillSignal signal,
        ILogger<BestEffortBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunBackfillAsync(stoppingToken);

        try
        {
            await foreach (var _ in _signal.ReadAllAsync(stoppingToken))
            {
                await RunBackfillAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunBackfillAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<BestEffortBackfillService>();
            await backfill.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Best effort backfill failed");
        }
    }
}
