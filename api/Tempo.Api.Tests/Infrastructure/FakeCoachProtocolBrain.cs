using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Test double for ICoachProtocolBrain. Configure responses via static/instance hooks.
/// </summary>
public sealed class FakeCoachProtocolBrain : ICoachProtocolBrain
{
    private int _callCount;

    public int CallCount => _callCount;

    /// <summary>
    /// Returns results in order; last result repeats if exhausted.
    /// </summary>
    public List<CoachProtocolBrainResult> Results { get; } = [];

    public Func<string, string?, CoachProtocolBrainResult>? Handler { get; set; }

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

    public void Reset()
    {
        _callCount = 0;
        Results.Clear();
        Handler = null;
    }
}
