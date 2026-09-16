using System.Threading.Channels;

namespace Tempo.Api.Services;

public sealed class IntervalsIcuSyncQueue
{
    private readonly Channel<bool> _channel = Channel.CreateUnbounded<bool>();

    public void TryWake() => _channel.Writer.TryWrite(true);

    public IAsyncEnumerable<bool> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
