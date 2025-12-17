using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for middleware pipeline configuration (CORS, Swagger, Authentication)
/// </summary>
public class MiddlewarePipelineTests
{
    [Fact]
    public async Task Cors_IsApplied_ForAllowedOrigins()
    {
        // Arrange
        using var factory = new TempoWebApplicationFactory();
        var client = factory.CreateClient();
        var origin = "http://localhost:3000";

        // Act - make request with Origin header
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Access-Control-Allow-Origin");
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain(origin);
    }

    [Fact]
    public async Task Cors_PreflightRequest_ReturnsAllowedMethods()
    {
        // Arrange
        using var factory = new TempoWebApplicationFactory();
        var client = factory.CreateClient();
        var origin = "http://localhost:3000";

        // Act - make OPTIONS preflight request
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.Should().ContainKey("Access-Control-Allow-Origin");
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain(origin);
        response.Headers.Should().ContainKey("Access-Control-Allow-Methods");
    }

    [Fact]
    public async Task Swagger_IsAvailable_InDevelopmentEnvironment()
    {
        // Arrange - save original environment
        // Save both JWT__SecretKey (double underscore, standard .NET convention) and JWT:SecretKey (colon, if it exists)
        var originalEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var originalJwtSecretDoubleUnderscore = Environment.GetEnvironmentVariable("JWT__SecretKey");
        var originalJwtSecretColon = Environment.GetEnvironmentVariable("JWT:SecretKey");
        var originalConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            // Set a valid JWT secret to allow startup
            Environment.SetEnvironmentVariable("JWT__SecretKey", "ValidSecretKeyForDevelopmentTesting12345678901234567890");
            // Also clear JWT:SecretKey if it exists to avoid conflicts
            if (originalJwtSecretColon != null)
            {
                Environment.SetEnvironmentVariable("JWT:SecretKey", null);
            }
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Data Source=:memory:?cache=shared");

            // Create factory with Development environment
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                });
            var client = factory.CreateClient();

            // Act
            var response = await client.GetAsync("/swagger/index.html");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadAsStringAsync();
            content.Should().ContainEquivalentOf("swagger");
        }
        finally
        {
            // Restore original environment variables (both variations)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnvironment);
            Environment.SetEnvironmentVariable("JWT__SecretKey", originalJwtSecretDoubleUnderscore);
            Environment.SetEnvironmentVariable("JWT:SecretKey", originalJwtSecretColon);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", originalConnectionString);
        }
    }

    [Fact]
    public async Task Swagger_IsNotAvailable_InProductionEnvironment()
    {
        // Arrange - save original environment
        // Save both JWT__SecretKey (double underscore, standard .NET convention) and JWT:SecretKey (colon, if it exists)
        var originalEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var originalJwtSecretDoubleUnderscore = Environment.GetEnvironmentVariable("JWT__SecretKey");
        var originalJwtSecretColon = Environment.GetEnvironmentVariable("JWT:SecretKey");
        var originalConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            // Set a valid JWT secret to allow startup
            Environment.SetEnvironmentVariable("JWT__SecretKey", "ValidSecretKeyForProductionTesting12345678901234567890");
            // Also clear JWT:SecretKey if it exists to avoid conflicts
            if (originalJwtSecretColon != null)
            {
                Environment.SetEnvironmentVariable("JWT:SecretKey", null);
            }
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Data Source=:memory:?cache=shared");

            // Create factory with Production environment
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                });
            var client = factory.CreateClient();

            // Act
            var response = await client.GetAsync("/swagger/index.html");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            // Restore original environment variables (both variations)
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnvironment);
            Environment.SetEnvironmentVariable("JWT__SecretKey", originalJwtSecretDoubleUnderscore);
            Environment.SetEnvironmentVariable("JWT:SecretKey", originalJwtSecretColon);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", originalConnectionString);
        }
    }

    [Fact]
    public async Task Authentication_IsRequired_ForProtectedEndpoints()
    {
        // Arrange
        using var factory = new TempoWebApplicationFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/workouts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthEndpoint_DoesNotRequireAuthentication()
    {
        // Arrange
        using var factory = new TempoWebApplicationFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("healthy");
    }

    [Fact]
    public async Task VersionEndpoint_DoesNotRequireAuthentication()
    {
        // Arrange
        using var factory = new TempoWebApplicationFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/version");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("version");
    }
}
