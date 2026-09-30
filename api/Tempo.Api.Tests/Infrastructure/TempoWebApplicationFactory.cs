using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Tempo.Api.Authentication;
using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory for integration testing.
/// Points the host at one Postgres clone per factory instance; does not replace host DbContext registration.
/// </summary>
public class TempoWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _tempMediaDirectory;
    private string? _cloneConnectionString;
    private bool _disposed;

    // Test JWT configuration
    private const string TestJwtSecretKey = "TestSecretKeyForJWTTokenGeneration12345678901234567890";
    private const string TestJwtIssuer = "Tempo-Test";
    private const string TestJwtAudience = "Tempo-Test";

    public TempoWebApplicationFactory()
    {
        // Set environment BEFORE creating builder so Program.cs can detect it
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

        // Create temporary directory for media storage
        _tempMediaDirectory = Path.Combine(Path.GetTempPath(), $"tempo-test-media-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempMediaDirectory);
    }

    private string EnsureCloneConnectionString()
    {
        if (_cloneConnectionString is null)
        {
            _cloneConnectionString = PostgresTestFixture.CreateCloneAsync().GetAwaiter().GetResult();
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _cloneConnectionString);
        return _cloneConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = EnsureCloneConnectionString();
        Environment.SetEnvironmentVariable("JWT__SecretKey", TestJwtSecretKey);
        Environment.SetEnvironmentVariable("JWT__Issuer", TestJwtIssuer);
        Environment.SetEnvironmentVariable("JWT__Audience", TestJwtAudience);
        Environment.SetEnvironmentVariable("MediaStorage__RootPath", _tempMediaDirectory);
        Environment.SetEnvironmentVariable("MediaStorage__MaxFileSizeBytes", "52428800");

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["JWT:SecretKey"] = TestJwtSecretKey,
                ["JWT:Issuer"] = TestJwtIssuer,
                ["JWT:Audience"] = TestJwtAudience,
                ["MediaStorage:RootPath"] = _tempMediaDirectory,
                ["MediaStorage:MaxFileSizeBytes"] = "52428800"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Do not remove or replace DbContext / Npgsql. Program.cs UseNpgsql uses the clone string.

            // Remove existing JWT configuration
            services.RemoveAll(typeof(IConfigureOptions<JwtBearerOptions>));

            // Override JWT configuration to match what JwtService will generate
            // JwtService reads from configuration, so we need to ensure both use the same values
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecretKey)),
                    ValidateIssuer = true,
                    ValidIssuer = TestJwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestJwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };
                options.Events = TempoJwtBearerEvents.Create();
            });

            // Override JwtService to use test values (ensures token generation matches validation)
            // Remove existing JwtService registration
            var jwtServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(JwtService));
            if (jwtServiceDescriptor != null)
            {
                services.Remove(jwtServiceDescriptor);
            }

            // Register JwtService with test configuration
            // Create a test configuration that provides test JWT values
            services.AddScoped<JwtService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<JwtService>>();
                var baseConfig = sp.GetRequiredService<IConfiguration>();

                // Create a configuration wrapper that overrides JWT values
                var testConfig = new TestJwtConfiguration(baseConfig, TestJwtSecretKey, TestJwtIssuer, TestJwtAudience);
                return new JwtService(testConfig, logger);
            });

            // Override MediaStorageConfig
            var mediaStorageConfigDescriptor = services.SingleOrDefault(s => s.ServiceType == typeof(MediaStorageConfig));
            if (mediaStorageConfigDescriptor != null)
            {
                services.Remove(mediaStorageConfigDescriptor);
            }
            services.AddSingleton(new MediaStorageConfig
            {
                RootPath = _tempMediaDirectory,
                MaxFileSizeBytes = 52_428_800 // 50MB
            });

            // Override ElevationCalculationConfig (keep defaults)
            var elevationConfigDescriptor = services.SingleOrDefault(s => s.ServiceType == typeof(ElevationCalculationConfig));
            if (elevationConfigDescriptor != null)
            {
                services.Remove(elevationConfigDescriptor);
            }
            services.AddSingleton(new ElevationCalculationConfig
            {
                NoiseThresholdMeters = 2.0,
                MinDistanceMeters = 10.0
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Point the host at the clone before Program.cs reads the connection string.
        EnsureCloneConnectionString();
        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            try
            {
                if (Directory.Exists(_tempMediaDirectory))
                {
                    Directory.Delete(_tempMediaDirectory, recursive: true);
                }
            }
            catch (Exception)
            {
                // Ignore cleanup errors
            }
        }

        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing && !_disposed)
            {
                if (_cloneConnectionString is not null)
                {
                    PostgresTestFixture.DropCloneAsync(_cloneConnectionString).GetAwaiter().GetResult();
                    _cloneConnectionString = null;
                }

                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Configuration wrapper that overrides JWT values for testing
/// </summary>
internal class TestJwtConfiguration : IConfiguration
{
    private readonly IConfiguration _baseConfig;
    private readonly string _jwtSecretKey;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;

    public TestJwtConfiguration(IConfiguration baseConfig, string jwtSecretKey, string jwtIssuer, string jwtAudience)
    {
        _baseConfig = baseConfig;
        _jwtSecretKey = jwtSecretKey;
        _jwtIssuer = jwtIssuer;
        _jwtAudience = jwtAudience;
    }

    public string? this[string key]
    {
        get
        {
            // Override JWT configuration values
            return key switch
            {
                "JWT:SecretKey" => _jwtSecretKey,
                "JWT:Issuer" => _jwtIssuer,
                "JWT:Audience" => _jwtAudience,
                _ => _baseConfig[key]
            };
        }
        set => _baseConfig[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => _baseConfig.GetChildren();
    public IChangeToken GetReloadToken() => _baseConfig.GetReloadToken();
    public IConfigurationSection GetSection(string key) => _baseConfig.GetSection(key);
}
