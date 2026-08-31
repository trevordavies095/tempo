namespace Tempo.Api.Services;

/// <summary>
/// Startup hosted service that backfills workout route previews after migrations have applied.
/// </summary>
public class RoutePreviewBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RoutePreviewBackfillWorker> _logger;

    public RoutePreviewBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<RoutePreviewBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<RoutePreviewBackfillService>();
            await backfill.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workout route preview backfill failed");
        }
    }
}
