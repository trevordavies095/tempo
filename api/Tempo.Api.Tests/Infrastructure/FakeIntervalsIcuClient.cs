using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

public sealed class FakeIntervalsIcuClient : IIntervalsIcuClient
{
    public IntervalsIcuProbeResult Result { get; set; } = IntervalsIcuProbeResult.Ok;

    public IntervalsIcuProbeResult ListStatus { get; set; } = IntervalsIcuProbeResult.Ok;

    public string? LastApiKey { get; private set; }

    public DateOnly? LastOldest { get; private set; }

    public int ProbeCount { get; private set; }

    public int ListCount { get; private set; }

    public int GetFileCount { get; private set; }

    public List<IntervalsIcuActivity> Activities { get; } = [];

    public Dictionary<string, IntervalsIcuActivityFile> Files { get; } = new(StringComparer.Ordinal);

    public Task<IntervalsIcuProbeResult> ProbeAthleteAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        LastApiKey = apiKey;
        ProbeCount++;
        return Task.FromResult(Result);
    }

    public Task<IntervalsIcuListResult> ListActivitiesAsync(
        string apiKey,
        DateOnly oldest,
        CancellationToken cancellationToken = default)
    {
        LastApiKey = apiKey;
        LastOldest = oldest;
        ListCount++;
        return Task.FromResult(new IntervalsIcuListResult
        {
            Status = ListStatus,
            Activities = ListStatus == IntervalsIcuProbeResult.Ok ? Activities.ToList() : []
        });
    }

    public Task<IntervalsIcuActivityFile?> GetActivityFileAsync(
        string apiKey,
        string activityId,
        CancellationToken cancellationToken = default)
    {
        LastApiKey = apiKey;
        GetFileCount++;
        Files.TryGetValue(activityId, out var file);
        return Task.FromResult(file);
    }

    public void Reset()
    {
        Result = IntervalsIcuProbeResult.Ok;
        ListStatus = IntervalsIcuProbeResult.Ok;
        LastApiKey = null;
        LastOldest = null;
        ProbeCount = 0;
        ListCount = 0;
        GetFileCount = 0;
        Activities.Clear();
        Files.Clear();
    }
}
