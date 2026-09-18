using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Testcontainers.PostgreSql;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Process-wide Testcontainers PostgreSQL 16. Started on first EF-backed use.
/// Migrates a template once, then clones per test. Parser tests must not call this type.
/// </summary>
public static class PostgresTestFixture
{
    public const string Image = "postgres:16-alpine";
    public const string TemplateDatabase = "tempo_test_template";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static string? _adminConnectionString;

    public static async Task EnsureStartedAsync()
    {
        if (_container is not null)
        {
            return;
        }

        await Gate.WaitAsync();
        try
        {
            if (_container is not null)
            {
                return;
            }

            var container = new PostgreSqlBuilder(Image)
                .WithDatabase("postgres")
                .Build();

            try
            {
                await container.StartAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "EF tests need Docker to start PostgreSQL 16 (Testcontainers).",
                    ex);
            }

            _adminConnectionString = container.GetConnectionString();
            await CreateAndMigrateTemplateAsync(_adminConnectionString);
            _container = container;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<string> CreateCloneAsync()
    {
        await EnsureStartedAsync();
        var name = $"tempo_test_{Guid.NewGuid():N}";
        await ExecuteAdminAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDatabase}\"");
        return WithDatabase(_adminConnectionString!, name);
    }

    public static async Task DropCloneAsync(string connectionString)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (string.IsNullOrWhiteSpace(name) ||
            name.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(TemplateDatabase, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await TerminateBackendsAsync(name);
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{name}\"");
    }

    public static TempoDbContext CreateContext(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<TempoDbContext>()
            .UseNpgsql(connectionString);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new TempoDbContext(builder.Options);
    }

    private static async Task CreateAndMigrateTemplateAsync(string adminConnectionString)
    {
        await ExecuteAdminAsync($"CREATE DATABASE \"{TemplateDatabase}\"");
        var templateCs = WithDatabase(adminConnectionString, TemplateDatabase);
        await using (var db = CreateContext(templateCs))
        {
            DatabaseMigrationHelper.ApplyMigrations(db);
        }

        await TerminateBackendsAsync(TemplateDatabase);
    }

    private static async Task TerminateBackendsAsync(string database)
    {
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = @name
              AND pid <> pg_backend_pid();
            """,
            conn);
        cmd.Parameters.AddWithValue("name", database);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAdminAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string WithDatabase(string connectionString, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = database
        };
        return builder.ConnectionString;
    }
}
