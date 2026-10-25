using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        _sut = CreateService(TestSecret);
    }

    private const string TestSecret = "test-secret-key-that-is-at-least-32-characters-long";

    private ApiKeyService CreateService(string jwtSecret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JWT:SecretKey"] = jwtSecret })
            .Build();
        return new ApiKeyService(_db, new PasswordService(), new ApiKeySecretProtector(config));
    }

    private async Task<User> SeedUserAsync(string username)
    {
        var user = new User { Username = username, PasswordHash = "h" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
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

        var (_, plaintext) = (await _sut.TryCreateAsync(user.Id, "l"))!.Value;

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

        var (entity, _) = (await _sut.TryCreateAsync(user.Id, null))!.Value;

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

        var (entity, plaintext) = (await _sut.TryCreateAsync(user.Id, null))!.Value;
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

        await _sut.TryCreateAsync(user.Id, null);

        var resolved = await _sut.TryGetActiveUserIdAsync(ApiKeyService.KeyMaterialPrefix + "wrongwrongwrongwrongwrongwrongwrongwrong");

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task TryGetActiveUserIdAsync_WithNonPrefix_ReturnsNull()
    {
        var resolved = await _sut.TryGetActiveUserIdAsync("not-a-tempo-key");

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task TryCreateAsync_WhenActiveKeyExists_ReturnsNull()
    {
        var user = await SeedUserAsync("u_one");
        (await _sut.TryCreateAsync(user.Id, "first")).Should().NotBeNull();

        var second = await _sut.TryCreateAsync(user.Id, "second");

        second.Should().BeNull();
        (await _db.ApiKeys.CountAsync(k => k.UserId == user.Id)).Should().Be(1);
    }

    [Fact]
    public async Task TryCreateAsync_AfterRevoke_Succeeds()
    {
        var user = await SeedUserAsync("u_rotate");
        var (first, _) = (await _sut.TryCreateAsync(user.Id, null))!.Value;
        await _sut.TryRevokeAsync(user.Id, first.Id);

        var second = await _sut.TryCreateAsync(user.Id, null);

        second.Should().NotBeNull();
        second!.Value.Entity.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public async Task TryCreateAsync_ActiveKeysAreIndependentPerUser()
    {
        var alice = await SeedUserAsync("u_alice");
        var bob = await SeedUserAsync("u_bob");

        (await _sut.TryCreateAsync(alice.Id, null)).Should().NotBeNull();
        (await _sut.TryCreateAsync(bob.Id, null)).Should().NotBeNull();
    }

    [Fact]
    public async Task ActiveKeyUniqueIndex_RejectsSecondActiveRow()
    {
        var user = await SeedUserAsync("u_index");
        (await _sut.TryCreateAsync(user.Id, null)).Should().NotBeNull();

        _db.ApiKeys.Add(new ApiKey
        {
            UserId = user.Id,
            KeyHash = "sha256:x",
            KeyPrefix = "tmp_bypass000000",
            CreatedAt = DateTime.UtcNow
        });

        await _db.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TryCreateAsync_StoresCiphertextNotPlaintext()
    {
        var user = await SeedUserAsync("u_cipher");

        var (entity, plaintext) = (await _sut.TryCreateAsync(user.Id, null))!.Value;

        entity.KeyCiphertext.Should().NotBeNullOrEmpty();
        System.Text.Encoding.UTF8.GetString(entity.KeyCiphertext!).Should().NotContain(plaintext);
    }

    [Fact]
    public async Task TryRevealAsync_ReturnsCreatedKey()
    {
        var user = await SeedUserAsync("u_reveal");
        var (entity, plaintext) = (await _sut.TryCreateAsync(user.Id, null))!.Value;

        var (status, revealed) = await _sut.TryRevealAsync(user.Id, entity.Id);

        status.Should().Be(ApiKeyRevealStatus.Revealed);
        revealed.Should().Be(plaintext);
    }

    [Fact]
    public async Task TryRevealAsync_WhenRevoked_ReturnsNotFound()
    {
        var user = await SeedUserAsync("u_reveal_revoked");
        var (entity, _) = (await _sut.TryCreateAsync(user.Id, null))!.Value;
        await _sut.TryRevokeAsync(user.Id, entity.Id);

        var (status, revealed) = await _sut.TryRevealAsync(user.Id, entity.Id);

        status.Should().Be(ApiKeyRevealStatus.NotFound);
        revealed.Should().BeNull();
    }

    [Fact]
    public async Task TryRevealAsync_ForOtherUsersKey_ReturnsNotFound()
    {
        var alice = await SeedUserAsync("u_reveal_alice");
        var bob = await SeedUserAsync("u_reveal_bob");
        var (entity, _) = (await _sut.TryCreateAsync(alice.Id, null))!.Value;

        var (status, _) = await _sut.TryRevealAsync(bob.Id, entity.Id);

        status.Should().Be(ApiKeyRevealStatus.NotFound);
    }

    [Fact]
    public async Task TryRevealAsync_WithoutCiphertext_ReturnsUnavailable()
    {
        var user = await SeedUserAsync("u_legacy_reveal");
        var (entity, _) = (await _sut.TryCreateAsync(user.Id, null))!.Value;
        await _db.ApiKeys.Where(k => k.Id == entity.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(k => k.KeyCiphertext, (byte[]?)null));

        var (status, revealed) = await _sut.TryRevealAsync(user.Id, entity.Id);

        status.Should().Be(ApiKeyRevealStatus.Unavailable);
        revealed.Should().BeNull();
    }

    [Fact]
    public async Task TryRevealAsync_AfterJwtSecretRotation_ReturnsUnavailableButKeyStillAuthenticates()
    {
        var user = await SeedUserAsync("u_rotated_secret");
        var (entity, plaintext) = (await _sut.TryCreateAsync(user.Id, null))!.Value;
        var rotated = CreateService("rotated-secret-key-that-is-at-least-32-characters");

        var (status, _) = await rotated.TryRevealAsync(user.Id, entity.Id);

        status.Should().Be(ApiKeyRevealStatus.Unavailable);
        (await rotated.TryGetActiveUserIdAsync(plaintext)).Should().Be(user.Id);
    }
}
