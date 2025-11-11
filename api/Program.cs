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
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:3001")
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

// Ensure database is created and migrations are applied
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
    db.Database.EnsureCreated();
    
    // Ensure WorkoutMedia table exists (apply migration manually if needed)
    try
    {
        // Mark InitialCreate as applied if needed
        db.Database.ExecuteSqlRaw(@"
            INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") 
            VALUES ('20251110232429_InitialCreate', '9.0.10') 
            ON CONFLICT (""MigrationId"") DO NOTHING;
        ");
        
        // Create WorkoutMedia table (IF NOT EXISTS handles case where it already exists)
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""WorkoutMedia"" (
                ""Id"" uuid NOT NULL,
                ""WorkoutId"" uuid NOT NULL,
                ""Filename"" character varying(500) NOT NULL,
                ""FilePath"" character varying(1000) NOT NULL,
                ""MimeType"" character varying(100) NOT NULL,
                ""FileSizeBytes"" bigint NOT NULL,
                ""Caption"" text,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                CONSTRAINT ""PK_WorkoutMedia"" PRIMARY KEY (""Id""),
                CONSTRAINT ""FK_WorkoutMedia_Workouts_WorkoutId"" FOREIGN KEY (""WorkoutId"") 
                    REFERENCES ""Workouts"" (""Id"") ON DELETE CASCADE
            );
        ");
        
        // Create index if it doesn't exist
        db.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS ""IX_WorkoutMedia_WorkoutId"" ON ""WorkoutMedia"" (""WorkoutId"");
        ");
        
        // Mark AddWorkoutMedia migration as applied
        db.Database.ExecuteSqlRaw(@"
            INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") 
            VALUES ('20251111023205_AddWorkoutMedia', '9.0.10') 
            ON CONFLICT (""MigrationId"") DO NOTHING;
        ");
        
        Log.Information("WorkoutMedia table migration check completed");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error applying WorkoutMedia migration - this may cause issues with media import");
    }
}

app.Run();
