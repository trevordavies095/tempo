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
using Microsoft.IdentityModel.Tokens;
using Tempo.Api.Authentication;
using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory pointed at a Postgres-shaped connection string that cannot connect.
/// Testing skips migrations, so the host starts without a live database.
/// </summary>
public sealed class UnreachablePostgresWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string Host = "127.0.0.1";
    public const string Username = "tempo_ready_probe_user_xyz";
    public const string Password = "tempo_ready_probe_secret_xyz";

    /// <summary>
    /// Closed port + short timeouts so CanConnectAsync fails within the readiness probe budget.
    /// </summary>
    public static readonly string ConnectionString =
        $"Host={Host};Port=1;Database=tempo_ready_probe;Username={Username};Password={Password};Timeout=2;Command Timeout=2";

    private const string TestJwtSecretKey = "TestSecretKeyForJWTTokenGeneration12345678901234567890";
    private const string TestJwtIssuer = "Tempo-Test";
    private const string TestJwtAudience = "Tempo-Test";

    private readonly string _tempMediaDirectory;
    private readonly string? _originalConnectionString;
    private readonly string? _originalJwtSecret;
    private readonly string? _originalJwtIssuer;
    private readonly string? _originalJwtAudience;
    private readonly string? _originalMediaRoot;
    private readonly string? _originalMediaMax;
    private readonly string? _originalEnvironment;

    public UnreachablePostgresWebApplicationFactory()
    {
        _originalEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        _originalConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        _originalJwtSecret = Environment.GetEnvironmentVariable("JWT__SecretKey");
        _originalJwtIssuer = Environment.GetEnvironmentVariable("JWT__Issuer");
        _originalJwtAudience = Environment.GetEnvironmentVariable("JWT__Audience");
        _originalMediaRoot = Environment.GetEnvironmentVariable("MediaStorage__RootPath");
        _originalMediaMax = Environment.GetEnvironmentVariable("MediaStorage__MaxFileSizeBytes");

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        _tempMediaDirectory = Path.Combine(Path.GetTempPath(), $"tempo-test-media-unreachable-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempMediaDirectory);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString);
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
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["JWT:SecretKey"] = TestJwtSecretKey,
                ["JWT:Issuer"] = TestJwtIssuer,
                ["JWT:Audience"] = TestJwtAudience,
                ["MediaStorage:RootPath"] = _tempMediaDirectory,
                ["MediaStorage:MaxFileSizeBytes"] = "52428800"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IConfigureOptions<JwtBearerOptions>));

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

            var jwtServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(JwtService));
            if (jwtServiceDescriptor != null)
            {
                services.Remove(jwtServiceDescriptor);
            }

            services.AddScoped<JwtService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<JwtService>>();
                var baseConfig = sp.GetRequiredService<IConfiguration>();
                var testConfig = new TestJwtConfiguration(baseConfig, TestJwtSecretKey, TestJwtIssuer, TestJwtAudience);
                return new JwtService(testConfig, logger);
            });

            var mediaStorageConfigDescriptor = services.SingleOrDefault(s => s.ServiceType == typeof(MediaStorageConfig));
            if (mediaStorageConfigDescriptor != null)
            {
                services.Remove(mediaStorageConfigDescriptor);
            }

            services.AddSingleton(new MediaStorageConfig
            {
                RootPath = _tempMediaDirectory,
                MaxFileSizeBytes = 52_428_800
            });

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
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString);
        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
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

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalEnvironment);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _originalConnectionString);
            Environment.SetEnvironmentVariable("JWT__SecretKey", _originalJwtSecret);
            Environment.SetEnvironmentVariable("JWT__Issuer", _originalJwtIssuer);
            Environment.SetEnvironmentVariable("JWT__Audience", _originalJwtAudience);
            Environment.SetEnvironmentVariable("MediaStorage__RootPath", _originalMediaRoot);
            Environment.SetEnvironmentVariable("MediaStorage__MaxFileSizeBytes", _originalMediaMax);
        }

        base.Dispose(disposing);
    }
}
