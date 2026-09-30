using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class ApiKeyServiceTests : IAsyncLifetime
{
    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ApiKeyService _sut = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _sut = new ApiKeyService(_db, new PasswordService());
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        if (_cloneConnectionString is not null)
        {
            await PostgresTestFixture.DropCloneAsync(_cloneConnectionString);
        }
    }

    [Fact]
    public async Task TryGetActiveUserIdAsync_WithValidKey_ReturnsUserId()
    {
        var user = new User { Username = "u1", PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (_, plaintext) = await _sut.CreateAsync(user.Id, "l");

        plaintext.Should().StartWith(ApiKeyService.KeyMaterialPrefix);
        var resolved = await _sut.TryGetActiveUserIdAsync(plaintext);

        resolved.Should().Be(user.Id);
    }

    [Fact]
    public async Task CreateAsync_StoresSha256PrefixedHash_NotBcrypt()
    {
        var user = new User { Username = "u_sha", PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (entity, _) = await _sut.CreateAsync(user.Id, null);

        entity.KeyHash.Should().StartWith("sha256:");
        entity.KeyHash.Should().NotStartWith("$2");
    }

    [Fact]
    public async Task TryAuthenticateUserAsync_LegacyBcryptStoredHash_StillWorks()
    {
        var user = new User { Username = "u_legacy", PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var plaintextKey = ApiKeyService.KeyMaterialPrefix + new string('x', 43);
        var bcryptHash = new PasswordService().HashPassword(plaintextKey);
        _db.ApiKeys.Add(new ApiKey
        {
            UserId = user.Id,
            KeyHash = bcryptHash,
            KeyPrefix = plaintextKey[..ApiKeyService.KeyPrefixLength],
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var resolved = await _sut.TryAuthenticateUserAsync(plaintextKey);

        resolved.Should().NotBeNull();
        resolved!.Id.Should().Be(user.Id);
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
