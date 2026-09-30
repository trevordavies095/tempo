using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tempo.Api.Services;

public sealed class IntervalsIcuClient : IIntervalsIcuClient
{
    public const string HttpClientName = "IntervalsIcu";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
            using var response = await SendAsync(apiKey, HttpMethod.Get, "athlete/0", cancellationToken);
            return ClassifyStatus(response.StatusCode);
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

    public async Task<IntervalsIcuListResult> ListActivitiesAsync(
        string apiKey,
        DateOnly oldest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var path = $"athlete/0/activities?oldest={oldest:yyyy-MM-dd}";
            using var response = await SendAsync(apiKey, HttpMethod.Get, path, cancellationToken);
            var status = ClassifyStatus(response.StatusCode);
            if (status != IntervalsIcuProbeResult.Ok)
            {
                return new IntervalsIcuListResult
                {
                    Status = status,
                    RetryAfter = ReadRetryAfter(response)
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var dtos = await JsonSerializer.DeserializeAsync<List<ActivityDto>>(stream, JsonOptions, cancellationToken)
                ?? [];

            var activities = dtos
                .Select(d => new IntervalsIcuActivity
                {
                    Id = ReadId(d.Id),
                    Type = d.Type,
                    FileType = d.FileType
                })
                .Where(a => !string.IsNullOrWhiteSpace(a.Id))
                .ToList();

            return new IntervalsIcuListResult { Status = IntervalsIcuProbeResult.Ok, Activities = activities };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "intervals.icu list failed");
            return new IntervalsIcuListResult { Status = IntervalsIcuProbeResult.Transient };
        }
    }

    public async Task<IntervalsIcuActivityFile?> GetActivityFileAsync(
        string apiKey,
        string activityId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsync(
                apiKey,
                HttpMethod.Get,
                $"activity/{Uri.EscapeDataString(activityId)}/file",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "intervals.icu file fetch returned {StatusCode}",
                    (int)response.StatusCode);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await DelayRetryAfterAsync(ReadRetryAfter(response), cancellationToken);
                }

                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var decompressed = MaybeDecompressGzip(bytes);

            var fileName = TryFileNameFromContentDisposition(response.Content.Headers.ContentDisposition);
            if (decompressed.WasGzip && fileName != null && fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^3];
            }

            return new IntervalsIcuActivityFile
            {
                FileName = string.IsNullOrWhiteSpace(fileName) ? "activity" : fileName,
                Bytes = decompressed.Bytes
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "intervals.icu file fetch failed");
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        string apiKey,
        HttpMethod method,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"API_KEY:{apiKey}")));
        return await client.SendAsync(request, cancellationToken);
    }

    private IntervalsIcuProbeResult ClassifyStatus(HttpStatusCode statusCode)
    {
        if ((int)statusCode is >= 200 and <= 299)
        {
            return IntervalsIcuProbeResult.Ok;
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return IntervalsIcuProbeResult.Unauthorized;
        }

        _logger.LogWarning("intervals.icu returned {StatusCode}", (int)statusCode);
        return IntervalsIcuProbeResult.Transient;
    }

    internal static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header == null)
        {
            return null;
        }

        if (header.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (header.Date is DateTimeOffset date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        return null;
    }

    internal static async Task DelayRetryAfterAsync(TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        if (retryAfter is not TimeSpan delay || delay <= TimeSpan.Zero)
        {
            return;
        }

        if (delay > TimeSpan.FromSeconds(30))
        {
            delay = TimeSpan.FromSeconds(30);
        }

        await Task.Delay(delay, cancellationToken);
    }

    private static string ReadId(JsonElement id)
    {
        return id.ValueKind switch
        {
            JsonValueKind.String => id.GetString() ?? string.Empty,
            JsonValueKind.Number => id.GetRawText(),
            _ => string.Empty
        };
    }

    private static (byte[] Bytes, bool WasGzip) MaybeDecompressGzip(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b)
        {
            return (bytes, false);
        }

        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return (output.ToArray(), true);
    }

    private static string? TryFileNameFromContentDisposition(ContentDispositionHeaderValue? disposition)
    {
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.Trim().Trim('"');
    }

    private sealed class ActivityDto
    {
        public JsonElement Id { get; set; }

        public string? Type { get; set; }

        [JsonPropertyName("file_type")]
        public string? FileType { get; set; }
    }
}
