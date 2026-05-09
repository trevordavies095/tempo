using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class ApiKeyPersistenceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;

    public ApiKeyPersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SaveChanges_PersistsApiKey_WithUserForeignKey()
    {
        var user = new User
        {
            Username = "apikey_user",
            PasswordHash = "hash",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var apiKey = new ApiKey
        {
            UserId = user.Id,
            Label = "CLI",
            KeyHash = "$2a$11$placeholderbcrypthashstringvalue",
            KeyPrefix = "tmp_ab12",
        };
        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();

        var loaded = await _db.ApiKeys.SingleAsync();
        loaded.UserId.Should().Be(user.Id);
        loaded.Label.Should().Be("CLI");
        loaded.KeyHash.Should().Be(apiKey.KeyHash);
        loaded.KeyPrefix.Should().Be("tmp_ab12");
        loaded.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task DeletingUser_CascadeDeletesApiKeys()
    {
        var user = new User
        {
            Username = "cascade_user",
            PasswordHash = "hash",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.ApiKeys.Add(new ApiKey
        {
            UserId = user.Id,
            KeyHash = "$2a$11$anotherplaceholderhashvaluehere",
            KeyPrefix = "tmp_xy99",
        });
        await _db.SaveChangesAsync();

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        (await _db.ApiKeys.AnyAsync()).Should().BeFalse();
    }
}
