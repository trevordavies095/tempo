namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Options for configuring the test database
/// </summary>
public enum TestDatabaseType
{
    /// <summary>
    /// SQLite in-memory database (fastest, default)
    /// </summary>
    SqliteInMemory,

    /// <summary>
    /// SQLite file-based database (for debugging)
    /// </summary>
    SqliteFile,

    /// <summary>
    /// PostgreSQL via Testcontainers (most realistic, requires Docker)
    /// </summary>
    Testcontainers
}

/// <summary>
/// Configuration options for test database
/// </summary>
public class TestDatabaseOptions
{
    /// <summary>
    /// Type of database to use for tests
    /// </summary>
    public TestDatabaseType DatabaseType { get; set; } = TestDatabaseType.SqliteInMemory;

    /// <summary>
    /// Gets the database type from environment variable or returns default
    /// </summary>
    public static TestDatabaseType GetDatabaseType()
    {
        var envValue = Environment.GetEnvironmentVariable("TEST_DATABASE_TYPE");
        if (string.IsNullOrWhiteSpace(envValue))
        {
            return TestDatabaseType.SqliteInMemory;
        }

        return envValue.ToLowerInvariant() switch
        {
            "sqliteinmemory" or "sqlite-in-memory" or "inmemory" => TestDatabaseType.SqliteInMemory,
            "sqlitefile" or "sqlite-file" or "file" => TestDatabaseType.SqliteFile,
            "testcontainers" or "postgres" or "postgresql" => TestDatabaseType.Testcontainers,
            _ => TestDatabaseType.SqliteInMemory
        };
    }
}
