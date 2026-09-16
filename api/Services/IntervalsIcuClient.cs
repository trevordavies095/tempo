using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Tempo.Api.Services;

public sealed class IntervalsIcuClient : IIntervalsIcuClient
{
    public const string HttpClientName = "IntervalsIcu";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IntervalsIcuClient> _logger;

    public IntervalsIcuClient(IHttpClientFactory httpClientFactory, ILogger<IntervalsIcuClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IntervalsIcuProbeResult> ProbeAthleteAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, "athlete/0");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"API_KEY:{apiKey}")));

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return IntervalsIcuProbeResult.Ok;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return IntervalsIcuProbeResult.Unauthorized;
            }

            _logger.LogWarning(
                "intervals.icu probe returned {StatusCode}",
                (int)response.StatusCode);
            return IntervalsIcuProbeResult.Transient;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "intervals.icu probe failed");
            return IntervalsIcuProbeResult.Transient;
        }
    }
}
