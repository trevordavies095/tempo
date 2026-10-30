namespace Tempo.Api.Services;

/// <summary>
/// Startup hosted service that fills per-workout best efforts after migrations have applied.
/// Does not block browsing; the all-time best efforts list keeps serving from stored records.
/// </summary>
public class BestEffortBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BestEffortBackfillWorker> _logger;

    public BestEffortBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<BestEffortBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
