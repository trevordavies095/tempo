namespace Tempo.Api.Services;

/// <summary>
/// Startup hosted service that backfills FIT device laps after migrations.
/// Does not block browsing; Workout overview stays usable while this runs.
/// </summary>
public class DeviceLapBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeviceLapBackfillWorker> _logger;

    public DeviceLapBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DeviceLapBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<DeviceLapBackfillService>();
            await backfill.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device lap backfill failed");
        }
    }
}
