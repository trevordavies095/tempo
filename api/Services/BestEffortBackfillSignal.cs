using System.Threading.Channels;

namespace Tempo.Api.Services;

/// <summary>
/// Asks <see cref="BestEffortBackfillWorker"/> for another backfill pass after startup (e.g. once a
/// Tempo ZIP restore has added unmarked workouts). Requests made while a pass is pending coalesce.
/// </summary>
public class BestEffortBackfillSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Request() => _channel.Writer.TryWrite(true);

    public IAsyncEnumerable<bool> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
