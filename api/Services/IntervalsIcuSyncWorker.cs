namespace Tempo.Api.Services;

public sealed class IntervalsIcuSyncWorker : BackgroundService
{
    private readonly IntervalsIcuSyncQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IntervalsIcuSyncWorker> _logger;

    public IntervalsIcuSyncWorker(
        IntervalsIcuSyncQueue queue,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<IntervalsIcuSyncWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = Math.Max(1, _configuration.GetValue("IntervalsIcu:PollIntervalMinutes", 15));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));

        var timerLoop = Task.Run(async () =>
        {
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    _queue.TryWake();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }, stoppingToken);

        try
        {
            await foreach (var _ in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var sync = scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>();
                    await sync.RunTickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Intervals.icu sync tick failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await timerLoop;
    }
}
