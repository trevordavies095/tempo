using System.Data;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Tempo.Api.Data;
using Tempo.Api.Endpoints;
using Tempo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

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
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
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
        ClockSkew = TimeSpan.Zero
    };
    // Support token from cookie
    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            context.Token = context.Request.Cookies["authToken"];
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Configure Entity Framework
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
// Check both the builder environment and the ASPNETCORE_ENVIRONMENT variable
var isTesting = builder.Environment.IsEnvironment("Testing") 
    || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing";
var isSqlite = connectionString?.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) == true;

// Always register TempoDbContext with the appropriate provider, unless already registered (for testing)
// The test factory will remove and re-register it, so we skip if already registered
if (!builder.Services.Any(s => s.ServiceType == typeof(TempoDbContext)))
{
    builder.Services.AddDbContext<TempoDbContext>(options =>
    {
        if (isSqlite)
        {
            options.UseSqlite(connectionString);
        }
        else
        {
            options.UseNpgsql(connectionString);
        }
    });
}

// Register services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<GpxParserService>();
builder.Services.AddScoped<StravaCsvParserService>();
builder.Services.AddScoped<FitParserService>();
builder.Services.AddScoped<MediaService>();
builder.Services.AddScoped<HeartRateZoneService>();
builder.Services.AddScoped<RelativeEffortService>();
builder.Services.AddScoped<BestEffortService>();
builder.Services.AddScoped<BulkImportService>();
builder.Services.AddScoped<SplitRecalculationService>();
builder.Services.AddScoped<WorkoutCropService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<ShoeMileageService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddHttpClient<WeatherService>();

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

app.UseSerilogRequestLogging();

// Map endpoints
app.MapAuthEndpoints();
app.MapWorkoutsEndpoints();
app.MapSettingsEndpoints();
app.MapShoesEndpoints();
app.MapStatsEndpoints();
app.MapVersionEndpoints();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Apply database migrations automatically on startup
try
{
    var migrationConnectionString = app.Configuration.GetConnectionString("DefaultConnection") 
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    var isSqliteForMigration = migrationConnectionString?.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) == true;
    var isTestingForMigration = app.Environment.IsEnvironment("Testing") 
        || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing";
    
    // Skip migrations in Testing environment (test factory handles schema creation)
    if (isTestingForMigration)
    {
        Log.Information("Skipping migrations for Testing environment (test factory handles schema creation)");
    }
    else if (isSqliteForMigration)
    {
        // For SQLite in non-testing environments, use EnsureCreated (migrations don't work well with SQLite)
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            db.Database.EnsureCreated();
        }
        Log.Information("SQLite database schema created using EnsureCreated");
    }
    else
    {
        // For PostgreSQL, use migrations
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
