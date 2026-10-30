namespace Tempo.Api.Services;

/// <summary>
/// Startup hosted service that fills stored HR zone seconds after migrations have applied.
/// Does not block browsing; Workout overview stays usable while this runs.
/// </summary>
public class ZoneSecondsBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ZoneSecondsBackfillWorker> _logger;

    public ZoneSecondsBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ZoneSecondsBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<ZoneSecondsBackfillService>();
            await backfill.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zone seconds backfill failed");
        }
    }
}
