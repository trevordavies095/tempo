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
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);
            var result = ClassifyStatus(response.StatusCode);
            if (result != AnthropicProbeResult.Ok)
            {
                _logger.LogWarning(
                    "Anthropic Coach probe returned {StatusCode} → {Result}",
                    (int)response.StatusCode,
                    result);
            }

            return result;
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

    public static AnthropicProbeResult ClassifyStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is >= 200 and <= 299)
        {
            return AnthropicProbeResult.Ok;
        }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AnthropicProbeResult.Unauthorized,
            HttpStatusCode.PaymentRequired => AnthropicProbeResult.Billing,
            (HttpStatusCode)429 => AnthropicProbeResult.Transient,
            >= HttpStatusCode.InternalServerError => AnthropicProbeResult.Transient,
            _ => AnthropicProbeResult.Transient
        };
    }
}
