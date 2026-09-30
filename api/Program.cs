using System.Data;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using Tempo.Api.Authentication;
using Tempo.Api.Authorization;
using Tempo.Api.Commands;
using Tempo.Api.Data;
using Tempo.Api.Endpoints;
using Tempo.Api.Logging;
using Tempo.Api.OpenApi;
using Tempo.Api.Services;

ResetPasswordArgs? resetCommand = null;
string? resetPassword = null;
if (ResetPasswordCommand.IsVerb(args))
{
    if (!ResetPasswordCommand.TryParse(args, out resetCommand, out var parseError))
    {
        Console.Error.WriteLine(parseError);
        Console.Error.WriteLine();
        Console.Error.WriteLine(ResetPasswordCommand.Usage);
        Environment.ExitCode = 1;
        return;
    }

    if (resetCommand.Help)
    {
        Console.Out.WriteLine(ResetPasswordCommand.Usage);
        return;
    }

    if (!ResetPasswordCommand.TryReadPassword(
            resetCommand,
            new ConsoleResetPasswordInput(),
            Console.Error,
            out resetPassword,
            out var readError))
    {
        Console.Error.WriteLine(readError);
        Environment.ExitCode = 1;
        return;
    }
}

var builder = WebApplication.CreateBuilder(resetCommand is not null ? [] : args);

// Named logging profile owns Serilog levels (not MEL Logging:LogLevel / Serilog__* overrides)
var loggingProfile = LoggingProfile.Parse(builder.Configuration[LoggingProfile.ConfigKey]);
Log.Logger = new LoggerConfiguration()
    .ApplyLevels(loggingProfile)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();
Log.Information("Logging profile: {Profile}", LoggingProfile.ToConfigName(loggingProfile));
builder.Services.AddSingleton(typeof(LoggingProfileKind), loggingProfile);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(TempoOpenApi.ConfigureSwaggerGen);

// Configure CORS
var corsOrigins = builder.Configuration["CORS:AllowedOrigins"] ?? "http://localhost:3000";
var origins = corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .SetPreflightMaxAge(TimeSpan.FromSeconds(86400));
    });
});

// Configure JWT Authentication
var jwtSecretKey = builder.Configuration["JWT:SecretKey"] 
    ?? throw new InvalidOperationException("JWT:SecretKey is not configured");

// Validate that the secret key is not the default placeholder value
// Skip validation in Testing environment (used by integration tests)
const string placeholderValue = "CHANGE_THIS_IN_PRODUCTION_USE_ENVIRONMENT_VARIABLE";
if (jwtSecretKey == placeholderValue && !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException(
        "JWT:SecretKey must be changed from the default placeholder value. " +
        "Set the JWT__SecretKey environment variable with a secure random key.");
}
var jwtIssuer = builder.Configuration["JWT:Issuer"] ?? "Tempo";
var jwtAudience = builder.Configuration["JWT:Audience"] ?? "Tempo";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = AuthenticationSchemes.TempoAuthentication;
    options.DefaultChallengeScheme = AuthenticationSchemes.TempoAuthentication;
})
.AddPolicyScheme(AuthenticationSchemes.TempoAuthentication, "Tempo JWT or API key", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        var auth = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth) &&
            auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = auth["Bearer ".Length..].Trim();
            if (token.StartsWith(ApiKeyService.KeyMaterialPrefix, StringComparison.Ordinal))
            {
                return AuthenticationSchemes.ApiKey;
            }
        }

        return JwtBearerDefaults.AuthenticationScheme;
    };
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5)
    };
    options.Events = TempoJwtBearerEvents.Create();
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthenticationSchemes.ApiKey, _ => { });

// JwtSessionOnly: interactive JWT only. Theme C API-key principals must omit jti so these routes stay admin/session-only.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicyNames.JwtSessionOnly, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(ClaimTypes.NameIdentifier);
        policy.RequireClaim(JwtRegisteredClaimNames.Jti);
    });
});

// Configure Entity Framework — PostgreSQL only. Fail before provider registration.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString) ||
    connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "PostgreSQL is required. ConnectionStrings:DefaultConnection must be a PostgreSQL connection string. " +
        "SQLite is not supported (including Data Source= strings).");
}

builder.Services.AddDbContext<TempoDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// Register services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TrackGeometry>();
builder.Services.AddScoped<TrackPointRehydration>();
builder.Services.AddScoped<GpxParserService>();
builder.Services.AddScoped<StravaCsvParserService>();
builder.Services.AddScoped<FitParserService>();
builder.Services.AddScoped<MediaService>();
builder.Services.AddScoped<HeartRateZoneService>();
builder.Services.AddScoped<SplitHeartRateService>();
builder.Services.AddScoped<RelativeEffortService>();
builder.Services.AddScoped<IRelativeEffortService>(sp => sp.GetRequiredService<RelativeEffortService>());
builder.Services.AddScoped<BestEffortService>();
builder.Services.AddScoped<IBestEffortService>(sp => sp.GetRequiredService<BestEffortService>());
builder.Services.AddScoped<BulkImportService>();
builder.Services.AddScoped<StravaBulkImportOrchestrator>();
builder.Services.AddSingleton<ImportJobQueue>();
builder.Services.AddScoped<ImportJobService>();
builder.Services.AddHostedService<ImportJobWorker>();
builder.Services.AddScoped<RoutePreviewBackfillService>();
builder.Services.AddHostedService<RoutePreviewBackfillWorker>();
builder.Services.AddScoped<SplitHeartRateBackfillService>();
builder.Services.AddHostedService<SplitHeartRateBackfillWorker>();
builder.Services.AddScoped<CadenceBackfillService>();
builder.Services.AddHostedService<CadenceBackfillWorker>();
builder.Services.AddScoped<TimerTimeBackfillService>();
builder.Services.AddHostedService<TimerTimeBackfillWorker>();
builder.Services.AddScoped<DeviceLapBackfillService>();
builder.Services.AddHostedService<DeviceLapBackfillWorker>();
builder.Services.AddScoped<SplitRecalculationService>();
builder.Services.AddScoped<WorkoutCropService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<ShoeMileageService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<RouteMatchingService>();
builder.Services.AddScoped<InsightsService>();
builder.Services.AddHttpClient<WeatherService>();
builder.Services.AddScoped<IWeatherService>(sp => sp.GetRequiredService<WeatherService>());
builder.Services.AddScoped<WorkoutIntake>();
builder.Services.AddScoped<HealthKitWorkoutDecoder>();
builder.Services.AddSingleton<IntervalsIcuSecretProtector>();
builder.Services.AddHttpClient(IntervalsIcuClient.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://intervals.icu/api/v1/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<IIntervalsIcuClient, IntervalsIcuClient>();
builder.Services.AddSingleton<IntervalsIcuSyncQueue>();
builder.Services.AddScoped<IntervalsIcuSyncService>();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<IntervalsIcuSyncWorker>();
}

// Configure media storage
var mediaRootPath = builder.Configuration["MediaStorage:RootPath"] ?? "./media";
var mediaRootFullPath = Path.IsPathRooted(mediaRootPath) 
    ? mediaRootPath 
    : Path.Combine(Directory.GetCurrentDirectory(), mediaRootPath);
Directory.CreateDirectory(mediaRootFullPath);
builder.Services.AddSingleton(new MediaStorageConfig
{
    RootPath = mediaRootFullPath,
    MaxFileSizeBytes = builder.Configuration.GetValue<long>("MediaStorage:MaxFileSizeBytes", 52_428_800) // 50MB default
});

// Configure elevation calculation
builder.Services.AddSingleton(new ElevationCalculationConfig
{
    NoiseThresholdMeters = builder.Configuration.GetValue<double>("ElevationCalculation:NoiseThresholdMeters", 2.0),
    MinDistanceMeters = builder.Configuration.GetValue<double>("ElevationCalculation:MinDistanceMeters", 10.0)
});

// Configure CARTO basemaps (optional; missing key serves watermarked tiles)
builder.Services.AddSingleton(new CartoBasemapsConfig
{
    ApiKey = builder.Configuration["CartoBasemaps:ApiKey"]?.Trim() ?? string.Empty
});

// Configure form options for large file uploads (bulk import)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500_000_000; // 500MB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

// Configure Kestrel server options for large request body size
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500_000_000; // 500MB
    options.Limits.MinRequestBodyDataRate = null; // Disable minimum data rate to prevent timeouts during slow uploads
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10); // Keep connections alive during long uploads
});

var app = builder.Build();

if (resetCommand is not null)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
    var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ResetPasswordCommand));
    Environment.ExitCode = await ResetPasswordCommand.ExecuteAsync(
        db,
        passwordService,
        logger,
        resetCommand.Username,
        resetPassword!,
        Console.Out,
        Console.Error);
    return;
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// CORS must be placed as early as possible, before any other middleware
// Minimal APIs handle routing implicitly, so explicit UseRouting() is not needed
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, exception) =>
        LoggingProfile.GetRequestLogLevel(
            loggingProfile,
            httpContext.Request.Path.Value,
            httpContext.Response.StatusCode,
            exception);
});

// Map endpoints
app.MapAuthEndpoints();
app.MapWorkoutsEndpoints();
app.MapSettingsEndpoints();
app.MapShoesEndpoints();
app.MapStatsEndpoints();
app.MapVersionEndpoints();
app.MapHealthEndpoints();

// Apply database migrations automatically on startup
try
{
    var isTestingForMigration = app.Environment.IsEnvironment("Testing")
        || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing";

    // Skip migrations in Testing environment (template/clone fixture owns schema)
    if (isTestingForMigration)
    {
        Log.Information("Skipping migrations for Testing environment (test fixture handles schema creation)");
    }
    else
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            DatabaseMigrationHelper.ApplyMigrations(db);
        }
        Log.Information("Database migrations completed successfully");
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Failed to apply database migrations. Application cannot start.");
    // Re-throw to prevent app from starting with broken database state
    // This will cause the container to exit, but with a clear error message
    throw;
}

app.Run();

// Make Program class accessible for WebApplicationFactory
public partial class Program { }
