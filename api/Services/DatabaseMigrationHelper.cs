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
    // Map of table names to their corresponding migration IDs
    private static readonly Dictionary<string, string> TableToMigrationMap = new()
    {
        // InitialCreate migration creates these tables
        { "Workouts", "20251111150526_InitialCreate" },
        { "WorkoutRoutes", "20251111150526_InitialCreate" },
        { "WorkoutSplits", "20251111150526_InitialCreate" },
        { "WorkoutTimeSeries", "20251111150526_InitialCreate" },
        { "WorkoutMedia", "20251111150526_InitialCreate" },
        // AddUserSettings migration
        { "UserSettings", "20251122003646_AddUserSettings" }
    };

    private const string ProductVersion = "9.0.10";

    /// <summary>
    /// Applies database migrations, handling edge cases where the database was created with EnsureCreated()
    /// or has tables that exist but aren't recorded in migration history.
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
            
            // Detect all existing tables and mark corresponding migrations as applied
            SyncMigrationHistory(db);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error checking migration state - will attempt to migrate anyway");
        }
        
        // Now apply any pending migrations
        try
        {
            db.Database.Migrate();
            Log.Information("Database migrations applied successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply database migrations");
            throw; // Re-throw to prevent app from starting with broken database state
        }
    }

    /// <summary>
    /// Synchronizes migration history by detecting existing tables and marking their migrations as applied.
    /// </summary>
    private static void SyncMigrationHistory(TempoDbContext db)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            // Get all existing tables in the public schema
            var existingTables = GetExistingTables(db);
            Log.Information("Found {Count} existing tables in database", existingTables.Count);

            // Get all recorded migrations
            var recordedMigrations = GetRecordedMigrations(db);
            Log.Information("Found {Count} recorded migrations in history", recordedMigrations.Count);

            // Determine which migrations should be marked as applied based on existing tables
            var migrationsToMark = new HashSet<string>();
            
            foreach (var table in existingTables)
            {
                if (TableToMigrationMap.TryGetValue(table, out var migrationId))
                {
                    if (!recordedMigrations.Contains(migrationId))
                    {
                        migrationsToMark.Add(migrationId);
                        Log.Information("Table '{Table}' exists but migration '{Migration}' is not recorded", table, migrationId);
                    }
                }
            }

            // Mark missing migrations as applied
            foreach (var migrationId in migrationsToMark)
            {
                try
                {
                    db.Database.ExecuteSqlRaw(@"
                        INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") 
                        VALUES ({0}, {1})
                        ON CONFLICT (""MigrationId"") DO NOTHING;
                    ", migrationId, ProductVersion);
                    Log.Information("Marked migration '{Migration}' as applied (table already exists)", migrationId);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to mark migration '{Migration}' as applied", migrationId);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error synchronizing migration history - will attempt to migrate anyway");
        }
    }

    /// <summary>
    /// Gets a list of all existing tables in the public schema.
    /// </summary>
    private static List<string> GetExistingTables(TempoDbContext db)
    {
        var tables = new List<string>();
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT table_name 
                FROM information_schema.tables 
                WHERE table_schema = 'public' 
                AND table_type = 'BASE TABLE'
                ORDER BY table_name;
            ";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var tableName = reader.GetString(0);
                tables.Add(tableName);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error getting existing tables");
        }

        return tables;
    }

    /// <summary>
    /// Gets a set of all migration IDs that are recorded in the migration history.
    /// </summary>
    private static HashSet<string> GetRecordedMigrations(TempoDbContext db)
    {
        var migrations = new HashSet<string>();
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ""MigrationId"" 
                FROM ""__EFMigrationsHistory"";
            ";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var migrationId = reader.GetString(0);
                migrations.Add(migrationId);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error getting recorded migrations");
        }

        return migrations;
    }
}

