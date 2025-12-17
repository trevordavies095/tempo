using System.Data;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tempo.Api.Data;
using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory for integration testing
/// </summary>
public class TempoWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly TestDatabaseOptions _databaseOptions;
    private readonly string _tempMediaDirectory;
    private readonly string? _tempDatabaseFile;
    private bool _disposed;

    // Test JWT configuration
    private const string TestJwtSecretKey = "TestSecretKeyForJWTTokenGeneration12345678901234567890";
    private const string TestJwtIssuer = "Tempo-Test";
    private const string TestJwtAudience = "Tempo-Test";

    public TempoWebApplicationFactory()
    {
        // Set environment BEFORE creating builder so Program.cs can detect it
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        
        _databaseOptions = new TestDatabaseOptions
        {
            DatabaseType = TestDatabaseOptions.GetDatabaseType()
        };

        // Create temporary directory for media storage
        _tempMediaDirectory = Path.Combine(Path.GetTempPath(), $"tempo-test-media-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempMediaDirectory);

        // Store database file path for cleanup if using SQLite file
        if (_databaseOptions.DatabaseType == TestDatabaseType.SqliteFile)
        {
            _tempDatabaseFile = Path.Combine(Path.GetTempPath(), $"tempo-test-db-{Guid.NewGuid()}.db");
        }
        
        // Set connection string environment variable early so Program.cs can detect SQLite
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", GetTestConnectionString());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Environment variables are already set in constructor, but ensure they're still set
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", GetTestConnectionString());
        Environment.SetEnvironmentVariable("JWT__SecretKey", TestJwtSecretKey);
        Environment.SetEnvironmentVariable("JWT__Issuer", TestJwtIssuer);
        Environment.SetEnvironmentVariable("JWT__Audience", TestJwtAudience);
        Environment.SetEnvironmentVariable("MediaStorage__RootPath", _tempMediaDirectory);
        Environment.SetEnvironmentVariable("MediaStorage__MaxFileSizeBytes", "52428800");
        
        builder.UseEnvironment("Testing");
        
        builder.ConfigureServices(services =>
        {
            // Remove ALL existing DbContext registrations (both options and the context itself)
            // Also remove any Npgsql-specific extension services that might conflict
            // This must happen AFTER Program.cs has run, so we remove what it registered
            var descriptorsToRemove = new List<ServiceDescriptor>();
            
            foreach (var service in services)
            {
                if (service.ServiceType == typeof(DbContextOptions<TempoDbContext>) ||
                    service.ServiceType == typeof(TempoDbContext) ||
                    (service.ServiceType != null && service.ServiceType.IsGenericType && 
                     service.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>)))
                {
                    descriptorsToRemove.Add(service);
                }
                // Also remove Npgsql-specific services if present
                else if (service.ServiceType != null && 
                         service.ServiceType.FullName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    descriptorsToRemove.Add(service);
                }
            }

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // Register test database context with SQLite
            var connectionString = GetTestConnectionString();
            services.AddDbContext<TempoDbContext>(options =>
            {
                options.UseSqlite(connectionString);
            }, ServiceLifetime.Scoped, ServiceLifetime.Scoped);

            // Remove existing JWT configuration
            services.RemoveAll(typeof(IConfigureOptions<JwtBearerOptions>));
            
            // Override JWT configuration
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
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        context.Token = context.Request.Cookies["authToken"];
                        return Task.CompletedTask;
                    }
                };
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
        var host = base.CreateHost(builder);
        
        // Initialize database after host is created
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<TempoWebApplicationFactory>>();

            try
            {
                // For SQLite, use EnsureCreated (migrations don't work well with SQLite)
                if (_databaseOptions.DatabaseType == TestDatabaseType.SqliteInMemory ||
                    _databaseOptions.DatabaseType == TestDatabaseType.SqliteFile)
                {
                    db.Database.EnsureCreated();
                    logger.LogInformation("Test database schema created using EnsureCreated");
                }
                else
                {
                    // For PostgreSQL, use migrations
                    DatabaseMigrationHelper.ApplyMigrations(db);
                    logger.LogInformation("Test database migrations applied");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred creating the test database");
                throw;
            }
        }
        
        return host;
    }

    private string GetTestConnectionString()
    {
        return _databaseOptions.DatabaseType switch
        {
            // Use shared-cache for in-memory SQLite so all connections share the same database
            // This ensures schema and data continuity across different scoped DbContext instances
            TestDatabaseType.SqliteInMemory => "Data Source=:memory:?cache=shared",
            TestDatabaseType.SqliteFile => $"Data Source={_tempDatabaseFile}",
            TestDatabaseType.Testcontainers => throw new NotImplementedException("Testcontainers support not yet implemented"),
            _ => "Data Source=:memory:?cache=shared"
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Clean up temporary media directory
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

            // Clean up SQLite database file if using file-based database
            if (_tempDatabaseFile != null && File.Exists(_tempDatabaseFile))
            {
                try
                {
                    File.Delete(_tempDatabaseFile);
                }
                catch (Exception)
                {
                    // Ignore cleanup errors
                }
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }
}
