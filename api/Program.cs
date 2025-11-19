using System.Data;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddSwaggerGen();

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
              .SetPreflightMaxAge(TimeSpan.FromSeconds(86400));
    });
});

// Configure Entity Framework
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<TempoDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register services
builder.Services.AddScoped<GpxParserService>();
builder.Services.AddScoped<StravaCsvParserService>();
builder.Services.AddScoped<FitParserService>();
builder.Services.AddScoped<MediaService>();
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
    options.Limits.RequestBodyTimeout = TimeSpan.FromMinutes(10); // Allow 10 minutes for large file uploads
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2); // Allow 2 minutes for multipart form headers
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

app.UseSerilogRequestLogging();

// Map endpoints
app.MapWorkoutsEndpoints();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Apply database migrations automatically on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
    
    // Handle migration state mismatch: if database was created with EnsureCreated(),
    // it has tables but no __EFMigrationsHistory table. We need to fix this first.
    try
    {
        // Create __EFMigrationsHistory table if it doesn't exist
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                ""MigrationId"" character varying(150) NOT NULL,
                ""ProductVersion"" character varying(32) NOT NULL,
                CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
            );
        ");
        
        // Check if Workouts table exists (indicates InitialCreate was applied via EnsureCreated)
        // Use connection to execute scalar query
        var workoutsTableExists = false;
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();
            
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) FROM information_schema.tables 
                WHERE table_schema = 'public' AND table_name = 'Workouts';
            ";
            var result = command.ExecuteScalar();
            workoutsTableExists = Convert.ToInt32(result) > 0;
        }
        catch
        {
            workoutsTableExists = false;
        }
        
        // Check if InitialCreate migration is recorded
        var initialCreateRecorded = false;
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();
            
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) FROM ""__EFMigrationsHistory""
                WHERE ""MigrationId"" = '20251110232429_InitialCreate';
            ";
            var result = command.ExecuteScalar();
            initialCreateRecorded = Convert.ToInt32(result) > 0;
        }
        catch
        {
            initialCreateRecorded = false;
        }
        
        // If tables exist but InitialCreate isn't recorded, mark it as applied
        if (workoutsTableExists && !initialCreateRecorded)
        {
            db.Database.ExecuteSqlRaw(@"
                INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") 
                VALUES ('20251110232429_InitialCreate', '9.0.10')
                ON CONFLICT (""MigrationId"") DO NOTHING;
            ");
            Log.Information("Marked InitialCreate migration as applied (tables existed from EnsureCreated)");
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Error checking migration state - will attempt to migrate anyway");
    }
    
    // Now apply any pending migrations (like AddWorkoutMedia)
    db.Database.Migrate();
    Log.Information("Database migrations applied successfully");
}

app.Run();
