using System.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Tempo.Api.Data;

namespace Tempo.Api.Services;

/// <summary>
/// Helper service for database migration operations.
/// </summary>
public static class DatabaseMigrationHelper
{
    /// <summary>
    /// Applies database migrations, handling edge cases where the database was created with EnsureCreated().
    /// </summary>
    public static void ApplyMigrations(TempoDbContext db)
    {
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
            var workoutsTableExists = CheckWorkoutsTableExists(db);
            
            // Check if InitialCreate migration is recorded
            var initialCreateRecorded = CheckInitialCreateRecorded(db);
            
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
        
        // Now apply any pending migrations
        db.Database.Migrate();
        Log.Information("Database migrations applied successfully");
    }

    private static bool CheckWorkoutsTableExists(TempoDbContext db)
    {
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
            return Convert.ToInt32(result) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckInitialCreateRecorded(TempoDbContext db)
    {
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
            return Convert.ToInt32(result) > 0;
        }
        catch
        {
            return false;
        }
    }
}

