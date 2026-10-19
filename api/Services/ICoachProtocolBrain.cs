namespace Tempo.Api.Services;

public enum CoachProtocolBrainError
{
    None,
    RateLimited,
    Auth,
    Billing,
    Timeout,
    Unknown
}

public sealed record CoachProtocolBrainResult(
    bool Success,
    string? CommitJson,
    int? InputTokens,
    int? OutputTokens,
    CoachProtocolBrainError Error,
    string? ErrorMessage);

/// <summary>
/// Fakeable protocol brain: redacted context packet → weekly commit or exchange reply JSON.
/// </summary>
public interface ICoachProtocolBrain
{
    Task<CoachProtocolBrainResult> GenerateWeeklyCommitAsync(
        string redactedPacketJson,
        string modelId,
        string apiKey,
        string? repairHint,
        CancellationToken cancellationToken = default);

    Task<CoachProtocolBrainResult> GenerateExchangeReplyAsync(
        string redactedPacketJson,
        string modelId,
        string apiKey,
        string? repairHint,
        CancellationToken cancellationToken = default);
}
