using System.Threading.Channels;

namespace Tempo.Api.Services;

public sealed class IntervalsIcuSyncQueue
{
    private readonly Channel<bool> _channel = Channel.CreateUnbounded<bool>();
    private readonly object _gate = new();
    private bool _pending;
    private bool _inFlight;

    public bool TryWake()
    {
        lock (_gate)
        {
            if (_inFlight || _pending)
            {
                return false;
            }

            _pending = true;
            return _channel.Writer.TryWrite(true);
        }
    }

    public void BeginTick()
    {
        lock (_gate)
        {
            _pending = false;
            _inFlight = true;
        }
    }

    public void EndTick()
    {
        lock (_gate)
        {
            _inFlight = false;
        }
    }

    public IAsyncEnumerable<bool> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    public void Reset()
    {
        lock (_gate)
        {
            _pending = false;
            _inFlight = false;
            while (_channel.Reader.TryRead(out _))
            {
            }
        }
    }
}
