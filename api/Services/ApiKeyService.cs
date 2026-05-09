using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// API key generation, storage (hashed), listing, revocation, and validation for machine auth (Theme C).
/// </summary>
public class ApiKeyService
{
    public const int KeyPrefixLength = 16;

    /// <summary>Plaintext keys use this prefix so the server can tell them apart from JWTs (see epic / Theme C).</summary>
    public const string KeyMaterialPrefix = "tmp_";

    private readonly TempoDbContext _db;
    private readonly PasswordService _passwordService;

    public ApiKeyService(TempoDbContext db, PasswordService passwordService)
    {
        _db = db;
        _passwordService = passwordService;
    }

    /// <summary>
    /// Creates a key; <paramref name="plaintextKey"/> is returned once to the caller — never stored.
    /// </summary>
    public async Task<(ApiKey Entity, string PlaintextKey)> CreateAsync(Guid userId, string? label, CancellationToken cancellationToken = default)
    {
        var plaintextKey = GenerateSecret();
        var keyPrefix = BuildKeyPrefix(plaintextKey);
        var entity = new ApiKey
        {
            UserId = userId,
            Label = NormalizeLabel(label),
            KeyHash = _passwordService.HashPassword(plaintextKey),
            KeyPrefix = keyPrefix,
            CreatedAt = DateTime.UtcNow
        };

        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return (entity, plaintextKey);
    }

    public async Task<IReadOnlyList<ApiKey>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _db.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Soft-revokes the key if it exists and belongs to <paramref name="userId"/>. Returns false if not found or not owned.
    /// </summary>
    public async Task<bool> TryRevokeAsync(Guid userId, Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ApiKeys.FirstOrDefaultAsync(
            k => k.Id == apiKeyId && k.UserId == userId,
            cancellationToken);

        if (entity == null)
        {
            return false;
        }

        if (entity.RevokedAt == null)
        {
            entity.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Resolves an active API key to the owning user (single query with user row). Theme C authentication handler.
    /// </summary>
    public async Task<User?> TryAuthenticateUserAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) ||
            !plaintextKey.StartsWith(KeyMaterialPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var prefix = BuildKeyPrefix(plaintextKey);
        var candidates = await _db.ApiKeys
            .Include(k => k.User)
            .Where(k => k.KeyPrefix == prefix && k.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var k in candidates)
        {
            if (_passwordService.VerifyPassword(plaintextKey, k.KeyHash))
            {
                return k.User;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves an active API key to a user id. Returns null if invalid or revoked.
    /// </summary>
    public async Task<Guid?> TryGetActiveUserIdAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        var user = await TryAuthenticateUserAsync(plaintextKey, cancellationToken);
        return user?.Id;
    }

    private static string GenerateSecret()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        var b64 = Convert.ToBase64String(buffer);
        var urlSafe = b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return KeyMaterialPrefix + urlSafe;
    }

    private static string BuildKeyPrefix(string plaintextKey)
    {
        return plaintextKey.Length <= KeyPrefixLength
            ? plaintextKey
            : plaintextKey[..KeyPrefixLength];
    }

    private static string? NormalizeLabel(string? label)
    {
        if (label == null)
        {
            return null;
        }

        var trimmed = label.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
