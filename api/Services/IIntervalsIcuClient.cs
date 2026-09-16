namespace Tempo.Api.Services;

public enum IntervalsIcuProbeResult
{
    Ok,
    Unauthorized,
    Transient
}

public sealed class IntervalsIcuActivity
{
    public required string Id { get; init; }
    public string? Type { get; init; }
    public string? FileType { get; init; }
}

public sealed class IntervalsIcuActivityFile
{
    public required string FileName { get; init; }
    public required byte[] Bytes { get; init; }
}

public sealed class IntervalsIcuListResult
{
    public required IntervalsIcuProbeResult Status { get; init; }
    public IReadOnlyList<IntervalsIcuActivity> Activities { get; init; } = [];
    public TimeSpan? RetryAfter { get; init; }
}

public interface IIntervalsIcuClient
{
    Task<IntervalsIcuProbeResult> ProbeAthleteAsync(string apiKey, CancellationToken cancellationToken = default);

    Task<IntervalsIcuListResult> ListActivitiesAsync(
        string apiKey,
        DateOnly oldest,
        CancellationToken cancellationToken = default);

    Task<IntervalsIcuActivityFile?> GetActivityFileAsync(
        string apiKey,
        string activityId,
        CancellationToken cancellationToken = default);
}
