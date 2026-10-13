namespace Tempo.Api.Services;

public enum AnthropicProbeResult
{
    Ok,
    Unauthorized,
    Billing,
    Transient
}

/// <summary>
/// Athlete-data-free Anthropic probe for Coach BYO key validation.
/// </summary>
public interface IAnthropicCoachClient
{
    Task<AnthropicProbeResult> ProbeApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}
