using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class AnthropicCoachClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, AnthropicProbeResult.Ok)]
    [InlineData(HttpStatusCode.NoContent, AnthropicProbeResult.Ok)]
    [InlineData(HttpStatusCode.Unauthorized, AnthropicProbeResult.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, AnthropicProbeResult.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired, AnthropicProbeResult.Billing)]
    [InlineData((HttpStatusCode)429, AnthropicProbeResult.Transient)]
    [InlineData(HttpStatusCode.InternalServerError, AnthropicProbeResult.Transient)]
    [InlineData(HttpStatusCode.BadRequest, AnthropicProbeResult.Transient)]
    public void ClassifyStatus_MapsExpectedResults(HttpStatusCode status, AnthropicProbeResult expected)
    {
        AnthropicCoachClient.ClassifyStatus(status).Should().Be(expected);
    }

    [Fact]
    public async Task ProbeApiKeyAsync_On200_ReturnsOk_AndSendsRequiredHeaders()
    {
        HttpRequestMessage? seen = null;
        var handler = new CaptureHandler(HttpStatusCode.OK, req => seen = req);
        var factory = new SingleClientFactory(handler);
        var client = new AnthropicCoachClient(factory, NullLogger<AnthropicCoachClient>.Instance);

        var result = await client.ProbeApiKeyAsync("sk-ant-test");

        result.Should().Be(AnthropicProbeResult.Ok);
        seen.Should().NotBeNull();
        seen!.Method.Should().Be(HttpMethod.Get);
        seen.RequestUri!.ToString().Should().Contain("v1/models");
        seen.Headers.GetValues("x-api-key").Single().Should().Be("sk-ant-test");
        seen.Headers.GetValues("anthropic-version").Single().Should().Be("2023-06-01");
    }

    [Fact]
    public async Task ProbeApiKeyAsync_On401_ReturnsUnauthorized()
    {
        var handler = new CaptureHandler(HttpStatusCode.Unauthorized);
        var client = new AnthropicCoachClient(
            new SingleClientFactory(handler),
            NullLogger<AnthropicCoachClient>.Instance);

        (await client.ProbeApiKeyAsync("bad")).Should().Be(AnthropicProbeResult.Unauthorized);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly Action<HttpRequestMessage>? _onSend;

        public CaptureHandler(HttpStatusCode status, Action<HttpRequestMessage>? onSend = null)
        {
            _status = status;
            _onSend = onSend;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _onSend?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.anthropic.com/")
            };
    }
}
