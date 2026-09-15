namespace Tempo.Api.Services;

/// <summary>
/// Startup hosted service that backfills Workout timer time and pace after migrations.
/// Does not block browsing; Workout overview stays usable while this runs.
/// </summary>
public class TimerTimeBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TimerTimeBackfillWorker> _logger;

    public TimerTimeBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<TimerTimeBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var backfill = scope.ServiceProvider.GetRequiredService<TimerTimeBackfillService>();
            await backfill.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Timer time backfill failed");
        }
    }
}
