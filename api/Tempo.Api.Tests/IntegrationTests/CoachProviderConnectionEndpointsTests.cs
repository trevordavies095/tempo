using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class CoachProviderConnectionEndpointsTests : IClassFixture<CoachProviderSettingsTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoachProviderSettingsTestFactory _factory;

    public CoachProviderConnectionEndpointsTests(CoachProviderSettingsTestFactory factory)
    {
        _factory = factory;
        _factory.Fake.Reset();
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        return await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory.Decorated);
    }

    [Fact]
    public async Task HostDisabled_ProviderMutations_ReturnNotFound()
    {
        using var disabled = new TempoWebApplicationFactory();
        using var scope = disabled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(disabled);

        (await client.GetAsync("/settings/coach/provider")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-x" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/settings/coach/provider/test", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync("/settings/coach/provider")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var settings = await client.GetFromJsonAsync<CoachSettingsResponse>("/settings/coach", JsonOptions);
        settings.Should().NotBeNull();
        settings!.Available.Should().BeFalse();
        settings.ProviderConnected.Should().BeFalse();
        settings.ModelId.Should().BeNull();
    }

    [Fact]
    public async Task Put_ValidKey_StoresCiphertext_NeverEchoes_DefaultModel_NoProbe()
    {
        var client = await AuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync(
            "/settings/coach/provider",
            new { apiKey = "sk-ant-plain-test-key" });
        var putJson = await put.Content.ReadAsStringAsync();

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        putJson.Should().NotContain("apiKey");
        putJson.Should().NotContain("sk-ant-plain-test-key");
        _factory.Fake.ProbeCount.Should().Be(0);

        var body = JsonSerializer.Deserialize<CoachProviderResponse>(putJson, JsonOptions);
        body.Should().NotBeNull();
        body!.Connected.Should().BeTrue();
        body.ModelId.Should().Be(CoachAnthropicModels.DefaultModelId);

        var get = await client.GetAsync("/settings/coach/provider");
        var getJson = await get.Content.ReadAsStringAsync();
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        getJson.Should().NotContain("sk-ant-plain-test-key");
        getJson.Should().NotContain("apiKey");

        var settings = await client.GetFromJsonAsync<CoachSettingsResponse>("/settings/coach", JsonOptions);
        settings!.ProviderConnected.Should().BeTrue();
        settings.ModelId.Should().Be(CoachAnthropicModels.DefaultModelId);

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var row = await db.CoachProviderConnections.SingleAsync();
        row.ApiKeyCiphertext.Should().NotBeEmpty();
        var plaintext = scope.ServiceProvider.GetRequiredService<CoachAnthropicSecretProtector>()
            .Decrypt(row.ApiKeyCiphertext);
        plaintext.Should().Be("sk-ant-plain-test-key");
    }

    [Fact]
    public async Task Put_UnknownModel_ReturnsBadRequest_WritesNoRow()
    {
        var client = await AuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync(
            "/settings/coach/provider",
            new { apiKey = "sk-ant-x", modelId = "gpt-4o" });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CoachProviderConnections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Put_SonnetModel_Persists()
    {
        var client = await AuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync(
            "/settings/coach/provider",
            new { apiKey = "sk-ant-x", modelId = CoachAnthropicModels.SonnetModelId });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await put.Content.ReadFromJsonAsync<CoachProviderResponse>(JsonOptions);
        body!.ModelId.Should().Be(CoachAnthropicModels.SonnetModelId);
    }

    [Fact]
    public async Task Put_ReplaceKey_KeepsConnectedAt()
    {
        var client = await AuthenticatedClientAsync();

        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-first" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        DateTime connectedAt;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            connectedAt = (await db.CoachProviderConnections.SingleAsync()).ConnectedAt;
        }

        await Task.Delay(20);
        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-second" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var row = await db.CoachProviderConnections.SingleAsync();
            row.ConnectedAt.Should().Be(connectedAt);
            scope.ServiceProvider.GetRequiredService<CoachAnthropicSecretProtector>()
                .Decrypt(row.ApiKeyCiphertext).Should().Be("sk-ant-second");
        }
    }

    [Fact]
    public async Task Test_WithBodyKey_ProbesWithoutPersisting()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = AnthropicProbeResult.Ok;

        var test = await client.PostAsJsonAsync(
            "/settings/coach/provider/test",
            new { apiKey = "sk-ant-ephemeral" });

        test.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Fake.ProbeCount.Should().Be(1);
        _factory.Fake.LastApiKey.Should().Be("sk-ant-ephemeral");

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CoachProviderConnections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Test_StoredKey_Unauthorized_UpdatesLastError()
    {
        var client = await AuthenticatedClientAsync();
        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-stored" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Fake.Result = AnthropicProbeResult.Unauthorized;
        var test = await client.PostAsJsonAsync("/settings/coach/provider/test", new { });

        test.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Fake.LastApiKey.Should().Be("sk-ant-stored");

        var get = await client.GetFromJsonAsync<CoachProviderResponse>("/settings/coach/provider", JsonOptions);
        get!.LastError.Should().Contain("rejected");
        get.LastProbeAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Test_NoStoredKey_ReturnsNotFound()
    {
        var client = await AuthenticatedClientAsync();

        var test = await client.PostAsJsonAsync("/settings/coach/provider/test", new { });

        test.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Fake.ProbeCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_ClearsProvider_KeepsOptIn()
    {
        var client = await AuthenticatedClientAsync();

        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-x" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var delete = await client.DeleteAsync("/settings/coach/provider");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CoachProviderConnections.CountAsync()).Should().Be(0);
        (await db.CoachPreferences.SingleAsync()).OptedIn.Should().BeTrue();

        var settings = await client.GetFromJsonAsync<CoachSettingsResponse>("/settings/coach", JsonOptions);
        settings!.OptedIn.Should().BeTrue();
        settings.ProviderConnected.Should().BeFalse();
    }

    [Fact]
    public async Task CoachHome_OmitsConnectStep_WhenProviderConnected()
    {
        var client = await AuthenticatedClientAsync();
        (await client.PostAsync("/settings/coach/opt-in", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync("/settings/coach/provider", new { apiKey = "sk-ant-x" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var home = await client.GetFromJsonAsync<CoachHomeResponse>("/coach", JsonOptions);
        home.Should().NotBeNull();
        home!.NextSteps.Should().NotContain(s => s.Contains("Anthropic", StringComparison.OrdinalIgnoreCase));
        home.NextSteps.Should().Contain(s => s.Contains("onboarding", StringComparison.OrdinalIgnoreCase));
    }

    private class CoachSettingsResponse
    {
        public bool Available { get; set; }
        public bool OptedIn { get; set; }
        public bool ProviderConnected { get; set; }
        public string? ModelId { get; set; }
    }

    private class CoachProviderResponse
    {
        public bool Connected { get; set; }
        public string? ModelId { get; set; }
        public DateTime? LastProbeAt { get; set; }
        public string? LastError { get; set; }
    }

    private class CoachHomeResponse
    {
        public List<string> NextSteps { get; set; } = new();
    }
}

public sealed class CoachProviderSettingsTestFactory : IDisposable
{
    private readonly TempoWebApplicationFactory _inner = new();

    public FakeAnthropicCoachClient Fake { get; } = new();

    public WebApplicationFactory<Program> Decorated { get; }

    public CoachProviderSettingsTestFactory()
    {
        Decorated = _inner.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var coachConfig = services.SingleOrDefault(s => s.ServiceType == typeof(CoachConfig));
                if (coachConfig != null)
                {
                    services.Remove(coachConfig);
                }

                services.AddSingleton(new CoachConfig { Enabled = true });
                services.RemoveAll<IAnthropicCoachClient>();
                services.AddSingleton<IAnthropicCoachClient>(Fake);
            });
        });
    }

    public void Dispose()
    {
        Decorated.Dispose();
        _inner.Dispose();
    }
}
