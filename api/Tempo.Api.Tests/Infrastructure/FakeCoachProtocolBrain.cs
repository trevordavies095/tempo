using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Test double for ICoachProtocolBrain. Configure responses via static/instance hooks.
/// </summary>
public sealed class FakeCoachProtocolBrain : ICoachProtocolBrain
{
    private int _callCount;
    private int _exchangeCallCount;

    public int CallCount => _callCount;

    public int ExchangeCallCount => _exchangeCallCount;

    /// <summary>
    /// Returns results in order; last result repeats if exhausted.
    /// </summary>
    public List<CoachProtocolBrainResult> Results { get; } = [];

    /// <summary>Exchange-specific results; falls back to <see cref="Results"/> when empty.</summary>
    public List<CoachProtocolBrainResult> ExchangeResults { get; } = [];

    public Func<string, string?, CoachProtocolBrainResult>? Handler { get; set; }

    public Func<string, string?, CoachProtocolBrainResult>? ExchangeHandler { get; set; }

    public Task<CoachProtocolBrainResult> GenerateWeeklyCommitAsync(
        string redactedPacketJson,
        string modelId,
        string apiKey,
        string? repairHint,
        CancellationToken cancellationToken = default)
    {
        _callCount++;
        if (Handler != null)
        {
            return Task.FromResult(Handler(redactedPacketJson, repairHint));
        }

        if (Results.Count == 0)
        {
            return Task.FromResult(new CoachProtocolBrainResult(
                false, null, null, null, CoachProtocolBrainError.Unknown, "Fake brain has no results"));
        }

        var index = Math.Min(_callCount - 1, Results.Count - 1);
        return Task.FromResult(Results[index]);
    }

    public Task<CoachProtocolBrainResult> GenerateExchangeReplyAsync(
        string redactedPacketJson,
        string modelId,
        string apiKey,
        string? repairHint,
        CancellationToken cancellationToken = default)
    {
        _exchangeCallCount++;
        _callCount++;
        if (ExchangeHandler != null)
        {
            return Task.FromResult(ExchangeHandler(redactedPacketJson, repairHint));
        }

        if (Handler != null)
        {
            return Task.FromResult(Handler(redactedPacketJson, repairHint));
        }

        var pool = ExchangeResults.Count > 0 ? ExchangeResults : Results;
        if (pool.Count == 0)
        {
            return Task.FromResult(new CoachProtocolBrainResult(
                false, null, null, null, CoachProtocolBrainError.Unknown, "Fake brain has no exchange results"));
        }

        var index = Math.Min(_exchangeCallCount - 1, pool.Count - 1);
        return Task.FromResult(pool[index]);
    }

    public void Reset()
    {
        _callCount = 0;
        _exchangeCallCount = 0;
        Results.Clear();
        ExchangeResults.Clear();
        Handler = null;
        ExchangeHandler = null;
    }
}
