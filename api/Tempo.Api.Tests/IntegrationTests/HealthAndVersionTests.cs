using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Health / ready / version HTTP contract. Version and image fields are read per-request from env,
/// so these cases reuse the shared factory (no extra Testcontainers clones).
/// Logging-profile formatting is covered by <see cref="VersionInfoTests"/>; booted profile is asserted as standard here.
/// </summary>
[Collection("Integration Tests")]
public class HealthAndVersionTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    /// <summary>Matches TempoWebApplicationFactory test JWT secret (not exported).</summary>
    private const string TestJwtSecretKey = "TestSecretKeyForJWTTokenGeneration12345678901234567890";

    public HealthAndVersionTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsOk_WithHealthyStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HealthResponse>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("healthy");
    }

    [Fact]
    public async Task GetReady_ReturnsOk_WhenDatabaseIsReachable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ReadyResponse>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("ready");
        result.Checks.Should().NotBeNull();
        result.Checks!.Database.Should().Be("ok");
    }

    [Fact]
    public async Task GetHealth_ReturnsOk_WhenDatabaseIsUnreachable()
    {
        using var factory = new UnreachablePostgresWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HealthResponse>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("healthy");
    }

    [Fact]
    public async Task GetReady_ReturnsServiceUnavailable_WhenDatabaseIsUnreachable()
    {
        using var factory = new UnreachablePostgresWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"not_ready\"");
        body.Should().Contain("\"database\":\"fail\"");
        body.Should().NotContain(UnreachablePostgresWebApplicationFactory.Host);
        body.Should().NotContain(UnreachablePostgresWebApplicationFactory.Username);
        body.Should().NotContain(UnreachablePostgresWebApplicationFactory.Password);

        var result = System.Text.Json.JsonSerializer.Deserialize<ReadyResponse>(
            body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        result.Should().NotBeNull();
        result!.Status.Should().Be("not_ready");
        result.Checks!.Database.Should().Be("fail");
    }

    [Fact]
    public async Task GetVersion_ReturnsVersionFromEnvironmentVariables_WhenSet()
    {
        using var _ = new TempoEnvScope(
            ("TEMPO_VERSION", "1.2.3-test"),
            ("TEMPO_BUILD_DATE", "2024-01-15T10:30:00Z"),
            ("TEMPO_GIT_COMMIT", "abc123def456"),
            ("TEMPO_API_IMAGE", null),
            ("TEMPO_FRONTEND_IMAGE", null));

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/version");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
        result.Should().NotBeNull();
        result!.Version.Should().Be("1.2.3-test");
        result.BuildDate.Should().Be("2024-01-15T10:30:00Z");
        result.GitCommit.Should().Be("abc123def456");
        AssertSupportSnapshotDefaults(result);
    }

    [Fact]
    public async Task GetVersion_ReturnsVersionFromFile_WhenEnvVarsNotSet()
    {
        using var _ = new TempoEnvScope(
            ("TEMPO_VERSION", null),
            ("TEMPO_BUILD_DATE", null),
            ("TEMPO_GIT_COMMIT", null),
            ("TEMPO_API_IMAGE", null),
            ("TEMPO_FRONTEND_IMAGE", null));

        var versionFilePath = Path.Combine(Directory.GetCurrentDirectory(), "VERSION");
        const string testVersion = "9.9.9-file-fallback";
        var previousContents = File.Exists(versionFilePath)
            ? await File.ReadAllTextAsync(versionFilePath)
            : null;
        await File.WriteAllTextAsync(versionFilePath, testVersion);

        try
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/version");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
            result.Should().NotBeNull();
            result!.Version.Should().Be(testVersion);
            result.BuildDate.Should().Be("unknown");
            result.GitCommit.Should().Be("unknown");
            AssertSupportSnapshotDefaults(result);
        }
        finally
        {
            if (previousContents is null)
            {
                File.Delete(versionFilePath);
            }
            else
            {
                await File.WriteAllTextAsync(versionFilePath, previousContents);
            }
        }
    }

    [Fact]
    public async Task GetVersion_EchoesImageEnvVars_WhenSet()
    {
        const string apiImage = "ghcr.io/trevordavies095/tempo/api:v2.9.0";
        const string frontendImage = "ghcr.io/trevordavies095/tempo/frontend:v2.9.0";
        using var _ = new TempoEnvScope(
            ("TEMPO_API_IMAGE", apiImage),
            ("TEMPO_FRONTEND_IMAGE", frontendImage));

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/version");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
        result.Should().NotBeNull();
        result!.ApiImage.Should().Be(apiImage);
        result.FrontendImage.Should().Be(frontendImage);
        result.Snapshot.Should().Be(ExpectedSnapshot(result));
    }

    [Fact]
    public async Task GetVersion_TreatsWhitespaceImageEnvAsUnknown()
    {
        using var _ = new TempoEnvScope(
            ("TEMPO_API_IMAGE", "   "),
            ("TEMPO_FRONTEND_IMAGE", "\t"));

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/version");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
        result.Should().NotBeNull();
        result!.ApiImage.Should().Be("unknown");
        result.FrontendImage.Should().Be("unknown");
        result.Snapshot.Should().Be(ExpectedSnapshot(result));
    }

    [Fact]
    public async Task GetVersion_BodyContainsNoSecrets()
    {
        using var _ = new TempoEnvScope(
            ("TEMPO_API_IMAGE", null),
            ("TEMPO_FRONTEND_IMAGE", null));

        var client = _factory.CreateClient();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        connectionString.Should().NotBeNullOrWhiteSpace();

        var response = await client.GetAsync("/version");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(TestJwtSecretKey);
        body.Should().NotContain(connectionString!);

        var result = System.Text.Json.JsonSerializer.Deserialize<VersionResponse>(
            body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        result.Should().NotBeNull();
        result!.Snapshot.Should().NotContain(TestJwtSecretKey);
        result.Snapshot.Should().NotContain(connectionString!);
        AssertSupportSnapshotDefaults(result);
    }

    private static void AssertSupportSnapshotDefaults(VersionResponse result)
    {
        result.LogProfile.Should().Be("standard");
        result.Environment.Should().Be("Testing");
        result.ApiImage.Should().Be("unknown");
        result.FrontendImage.Should().Be("unknown");
        result.Snapshot.Should().NotBeNullOrWhiteSpace();
        result.Snapshot.Should().Be(ExpectedSnapshot(result));
    }

    private static string ExpectedSnapshot(VersionResponse result) =>
        VersionInfo.FormatSnapshot(
            result.Version,
            result.GitCommit,
            result.BuildDate,
            result.LogProfile,
            result.Environment,
            result.ApiImage,
            result.FrontendImage);

    private sealed class TempoEnvScope : IDisposable
    {
        private readonly (string Key, string? Previous)[] _previous;

        public TempoEnvScope(params (string Key, string? Value)[] values)
        {
            _previous = values
                .Select(v => (v.Key, Environment.GetEnvironmentVariable(v.Key)))
                .ToArray();
            foreach (var (key, value) in values)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, previous) in _previous)
            {
                Environment.SetEnvironmentVariable(key, previous);
            }
        }
    }

    private class HealthResponse
    {
        public string Status { get; set; } = string.Empty;
    }

    private class ReadyResponse
    {
        public string Status { get; set; } = string.Empty;
        public ReadyChecks? Checks { get; set; }
    }

    private class ReadyChecks
    {
        public string Database { get; set; } = string.Empty;
    }

    private class VersionResponse
    {
        public string Version { get; set; } = string.Empty;
        public string BuildDate { get; set; } = string.Empty;
        public string GitCommit { get; set; } = string.Empty;
        public string LogProfile { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string ApiImage { get; set; } = string.Empty;
        public string FrontendImage { get; set; } = string.Empty;
        public string Snapshot { get; set; } = string.Empty;
    }
}
