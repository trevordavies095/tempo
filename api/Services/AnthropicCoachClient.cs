using System.Net;
using System.Net.Http.Headers;

namespace Tempo.Api.Services;

/// <summary>
/// Lightweight Anthropic auth probe (GET /v1/models). Never sends athlete training data.
/// </summary>
public sealed class AnthropicCoachClient : IAnthropicCoachClient
{
    public const string HttpClientName = "AnthropicCoach";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnthropicCoachClient> _logger;

    public AnthropicCoachClient(IHttpClientFactory httpClientFactory, ILogger<AnthropicCoachClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<AnthropicProbeResult> ProbeApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, "v1/models");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);
            return ClassifyStatus(response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Anthropic Coach probe failed");
            return AnthropicProbeResult.Transient;
        }
    }

    private static AnthropicProbeResult ClassifyStatus(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.OK => AnthropicProbeResult.Ok,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AnthropicProbeResult.Unauthorized,
            HttpStatusCode.PaymentRequired => AnthropicProbeResult.Billing,
            (HttpStatusCode)429 => AnthropicProbeResult.Transient,
            >= HttpStatusCode.InternalServerError => AnthropicProbeResult.Transient,
            _ => AnthropicProbeResult.Transient
        };
    }
}
