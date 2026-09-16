using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

public sealed class FakeIntervalsIcuClient : IIntervalsIcuClient
{
    public IntervalsIcuProbeResult Result { get; set; } = IntervalsIcuProbeResult.Ok;

    public string? LastApiKey { get; private set; }

    public int ProbeCount { get; private set; }

    public Task<IntervalsIcuProbeResult> ProbeAthleteAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        LastApiKey = apiKey;
        ProbeCount++;
        return Task.FromResult(Result);
    }

    public void Reset()
    {
        Result = IntervalsIcuProbeResult.Ok;
        LastApiKey = null;
        ProbeCount = 0;
    }
}
