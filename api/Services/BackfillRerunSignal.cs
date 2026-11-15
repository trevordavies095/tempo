using System.Threading.Channels;

namespace Tempo.Api.Services;

/// <summary>
/// Asks <see cref="BackfillRunner"/> for a full backfill pass after startup (once a Tempo ZIP restore
/// has added workouts that may lack derived fields). Requests made while a pass is pending coalesce.
/// </summary>
public class BackfillRerunSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Request() => _channel.Writer.TryWrite(true);

    public IAsyncEnumerable<bool> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
