using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class IntervalsIcuSettingsEndpointsTests : IClassFixture<IntervalsIcuSettingsTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IntervalsIcuSettingsTestFactory _factory;

    public IntervalsIcuSettingsEndpointsTests(IntervalsIcuSettingsTestFactory factory)
    {
        _factory = factory;
        _factory.Fake.Reset();
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        await EnsureCleanDatabaseAsync();
        return await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory.Decorated);
    }

    private async Task EnsureCleanDatabaseAsync()
    {
        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
    }

    [Fact]
    public async Task Get_WithNoRow_ReturnsDisconnected()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync("/settings/intervals-icu");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("apiKey");
        var body = JsonSerializer.Deserialize<IntervalsIcuStatusResponse>(json, JsonOptions);
        body.Should().NotBeNull();
        body!.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task Put_WithValidKey_PersistsCiphertextAndOmitsKeyOnGet()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;

        var put = await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "plain-icu-key" });
        var putJson = await put.Content.ReadAsStringAsync();

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        putJson.Should().NotContain("apiKey");
        putJson.Should().NotContain("plain-icu-key");
        var created = JsonSerializer.Deserialize<IntervalsIcuStatusResponse>(putJson, JsonOptions);
        created.Should().NotBeNull();
        created!.Connected.Should().BeTrue();
        created.Enabled.Should().BeTrue();

        var get = await client.GetAsync("/settings/intervals-icu");
        var getJson = await get.Content.ReadAsStringAsync();
        getJson.Should().NotContain("apiKey");
        getJson.Should().NotContain("plain-icu-key");
        var status = JsonSerializer.Deserialize<IntervalsIcuStatusResponse>(getJson, JsonOptions);
        status!.Connected.Should().BeTrue();
        status.Enabled.Should().BeTrue();

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var row = await db.IntervalsIcuConnections.SingleAsync();
        row.ApiKeyCiphertext.Should().NotBeEmpty();
        System.Text.Encoding.UTF8.GetString(row.ApiKeyCiphertext).Should().NotContain("plain-icu-key");
        row.Enabled.Should().BeTrue();
        _factory.Fake.LastApiKey.Should().Be("plain-icu-key");
    }

    [Fact]
    public async Task Put_WhenProbeUnauthorized_Returns400AndWritesNoRow()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Unauthorized;

        var put = await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "bad-key" });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.IntervalsIcuConnections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Put_WhenAlreadyConnected_Returns409()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
        (await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "first-key" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "second-key" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.Fake.ProbeCount.Should().Be(1);
    }

    [Fact]
    public async Task Put_WhenBlankKey_Returns400()
    {
        var client = await AuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "   " });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Fake.ProbeCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_RemovesRow_AndSecondDeleteIs204()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
        (await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "plain-icu-key" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await client.DeleteAsync("/settings/intervals-icu");
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.IntervalsIcuConnections.CountAsync()).Should().Be(0);
        }

        var second = await client.DeleteAsync("/settings/intervals-icu");
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Unauthenticated_GetPutDelete_Return401()
    {
        await EnsureCleanDatabaseAsync();
        var client = TestHttpClientFactory.CreateUnauthenticatedClient(_factory.Decorated);

        (await client.GetAsync("/settings/intervals-icu")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync("/settings/intervals-icu")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class IntervalsIcuStatusResponse
    {
        public bool Connected { get; set; }
        public bool? Enabled { get; set; }
        public DateTime? LastSuccessfulSyncAt { get; set; }
        public DateTime? LastSyncAttemptAt { get; set; }
        public string? LastError { get; set; }
    }
}

public sealed class IntervalsIcuSettingsTestFactory : IDisposable
{
    private readonly TempoWebApplicationFactory _inner = new();

    public FakeIntervalsIcuClient Fake { get; } = new();

    public WebApplicationFactory<Program> Decorated { get; }

    public IntervalsIcuSettingsTestFactory()
    {
        Decorated = _inner.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIntervalsIcuClient>();
                services.AddSingleton<IIntervalsIcuClient>(Fake);
            });
        });
    }

    public void Dispose()
    {
        Decorated.Dispose();
        _inner.Dispose();
    }
}
