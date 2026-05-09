using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class ApiKeyServiceTests : IDisposable
{
    private readonly TempoDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly ApiKeyService _sut;

    public ApiKeyServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TempoDbContext(options);
        _db.Database.EnsureCreated();

        var passwordService = new PasswordService();
        _sut = new ApiKeyService(_db, passwordService);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task TryGetActiveUserIdAsync_WithValidKey_ReturnsUserId()
    {
        var user = new User { Username = "u1", PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (_, plaintext) = await _sut.CreateAsync(user.Id, "l");

        var resolved = await _sut.TryGetActiveUserIdAsync(plaintext);

        resolved.Should().Be(user.Id);
    }

    [Fact]
    public async Task TryGetActiveUserIdAsync_WhenRevoked_ReturnsNull()
    {
        var user = new User { Username = "u2", PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (entity, plaintext) = await _sut.CreateAsync(user.Id, null);
        await _sut.TryRevokeAsync(user.Id, entity.Id);

        var resolved = await _sut.TryGetActiveUserIdAsync(plaintext);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task TryGetActiveUserIdAsync_WithWrongSecret_ReturnsNull()
    {
        var user = new User { Username = "u3", PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await _sut.CreateAsync(user.Id, null);

        var resolved = await _sut.TryGetActiveUserIdAsync(ApiKeyService.KeyMaterialPrefix + "wrongwrongwrongwrongwrongwrongwrongwrong");

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task TryGetActiveUserIdAsync_WithNonPrefix_ReturnsNull()
    {
        var resolved = await _sut.TryGetActiveUserIdAsync("not-a-tempo-key");

        resolved.Should().BeNull();
    }
}
