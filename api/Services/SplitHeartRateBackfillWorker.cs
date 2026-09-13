namespace Tempo.Api.Services;

/// <summary>
/// Startup hosted service that stamps per-split average heart rate after migrations have applied.
/// Does not block browsing; Workout overview stays usable while this runs.
/// </summary>
public class SplitHeartRateBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SplitHeartRateBackfillWorker> _logger;

    public SplitHeartRateBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SplitHeartRateBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<SplitHeartRateBackfillService>();
            await backfill.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Split heart rate backfill failed");
        }
    }
}
