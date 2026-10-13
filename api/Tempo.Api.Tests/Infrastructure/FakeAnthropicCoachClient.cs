using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

public sealed class FakeAnthropicCoachClient : IAnthropicCoachClient
{
    public AnthropicProbeResult Result { get; set; } = AnthropicProbeResult.Ok;

    public string? LastApiKey { get; private set; }

    public int ProbeCount { get; private set; }

    public Task<AnthropicProbeResult> ProbeApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        LastApiKey = apiKey;
        ProbeCount++;
        return Task.FromResult(Result);
    }

    public void Reset()
    {
        Result = AnthropicProbeResult.Ok;
        LastApiKey = null;
        ProbeCount = 0;
    }
}
