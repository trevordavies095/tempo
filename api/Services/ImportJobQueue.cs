using System.Threading.Channels;

namespace Tempo.Api.Services;

public class ImportJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
