using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tempo.Api.Tests;

/// <summary>
/// Unit-style tests for API startup configuration validation
/// These tests verify that Program.cs validation logic works correctly
/// 
/// Uses a test collection to ensure isolation from other tests that may set environment variables
/// </summary>
[Collection("Startup Configuration Tests")]
public class StartupConfigurationTests
{
    [Fact(Skip = "This test is difficult to implement with WebApplicationFactory because it always loads appsettings.json before ConfigureAppConfiguration runs. " +
                  "The placeholder validation test (Startup_ThrowsException_WhenJwtSecretKeyIsPlaceholder) provides sufficient coverage for the validation logic.")]
    public void Startup_ThrowsException_WhenJwtSecretKeyIsMissing()
    {
        // This test would verify that Program.cs throws when JWT:SecretKey is completely missing.
        // However, WebApplicationFactory loads appsettings.json by default before ConfigureAppConfiguration
        // can remove it, making it impossible to test a truly "missing" configuration value.
        // The placeholder validation test provides sufficient coverage for the validation logic.
    }

    [Fact]
    public void Startup_ThrowsException_WhenJwtSecretKeyIsPlaceholder()
    {
        // Arrange - save original environment variables
        // Save both JWT__SecretKey (double underscore, standard .NET convention) and JWT:SecretKey (colon, if it exists)
        var originalJwtSecretDoubleUnderscore = Environment.GetEnvironmentVariable("JWT__SecretKey");
        var originalJwtSecretColon = Environment.GetEnvironmentVariable("JWT:SecretKey");
        var originalConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var originalEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        
        try
        {
            // Set placeholder value
            const string placeholderValue = "CHANGE_THIS_IN_PRODUCTION_USE_ENVIRONMENT_VARIABLE";
            Environment.SetEnvironmentVariable("JWT__SecretKey", placeholderValue);
            // Also clear JWT:SecretKey if it exists to avoid conflicts
            if (originalJwtSecretColon != null)
            {
                Environment.SetEnvironmentVariable("JWT:SecretKey", null);
            }
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Data Source=:memory:");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

            // Act - try to create the application factory with Production environment
            // This will execute Program.cs which will throw during configuration
            var act = () =>
            {
                using var factory = new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseEnvironment("Production");
                    });
                // Access Server to trigger application startup
                _ = factory.Server;
            };

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*must be changed from the default placeholder value*");
        }
        finally
        {
            // Restore original environment variables (both variations)
            Environment.SetEnvironmentVariable("JWT__SecretKey", originalJwtSecretDoubleUnderscore);
            Environment.SetEnvironmentVariable("JWT:SecretKey", originalJwtSecretColon);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", originalConnectionString);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnvironment);
        }
    }

    [Fact]
    public void Startup_Succeeds_WhenJwtSecretKeyIsPlaceholderInTestingEnvironment()
    {
        // Arrange - save original environment variables
        // Save both JWT__SecretKey (double underscore, standard .NET convention) and JWT:SecretKey (colon, if it exists)
        var originalJwtSecretDoubleUnderscore = Environment.GetEnvironmentVariable("JWT__SecretKey");
        var originalJwtSecretColon = Environment.GetEnvironmentVariable("JWT:SecretKey");
        var originalConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var originalEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        
        try
        {
            // Set placeholder value
            const string placeholderValue = "CHANGE_THIS_IN_PRODUCTION_USE_ENVIRONMENT_VARIABLE";
            Environment.SetEnvironmentVariable("JWT__SecretKey", placeholderValue);
            // Also clear JWT:SecretKey if it exists to avoid conflicts
            if (originalJwtSecretColon != null)
            {
                Environment.SetEnvironmentVariable("JWT:SecretKey", null);
            }
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Data Source=:memory:?cache=shared");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

            // Act - try to create the application factory with Testing environment
            // This should succeed because validation is skipped in Testing environment
            var act = () =>
            {
                using var factory = new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseEnvironment("Testing");
                    });
                // Access Server to trigger application startup
                _ = factory.Server;
            };

            // Assert - should not throw exception
            act.Should().NotThrow();
        }
        finally
        {
            // Restore original environment variables (both variations)
            Environment.SetEnvironmentVariable("JWT__SecretKey", originalJwtSecretDoubleUnderscore);
            Environment.SetEnvironmentVariable("JWT:SecretKey", originalJwtSecretColon);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", originalConnectionString);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnvironment);
        }
    }
}
