namespace Tempo.Api.Services;

/// <summary>
/// Startup hosted service that rewrites FIT cadence to steps/min after migrations have applied.
/// Does not block browsing; Workout overview stays usable while this runs.
/// </summary>
public class CadenceBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CadenceBackfillWorker> _logger;

    public CadenceBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<CadenceBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<CadenceBackfillService>();
            await backfill.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIT cadence backfill failed");
        }
    }
}
