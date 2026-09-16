namespace Tempo.Api.Services;

public enum IntervalsIcuProbeResult
{
    Ok,
    Unauthorized,
    Transient
}

public interface IIntervalsIcuClient
{
    Task<IntervalsIcuProbeResult> ProbeAthleteAsync(string apiKey, CancellationToken cancellationToken = default);
}
