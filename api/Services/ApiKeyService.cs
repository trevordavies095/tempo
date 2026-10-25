using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public enum ApiKeyRevealStatus
{
    Revealed,
    NotFound,

    /// <summary>Key exists and is active but has no readable ciphertext (pre-reveal key or rotated JWT:SecretKey).</summary>
    Unavailable
}

/// <summary>
/// API key generation, storage (hashed + encrypted), listing, reveal, revocation, and validation for machine auth (Theme C).
/// Each user has at most one active key.
/// </summary>
public class ApiKeyService
{
    public const int KeyPrefixLength = 16;

    /// <summary>Plaintext keys use this prefix so the server can tell them apart from JWTs (see epic / Theme C).</summary>
    public const string KeyMaterialPrefix = "tmp_";

    /// <summary>Stored <see cref="ApiKey.KeyHash"/> values use this prefix for SHA-256 (high-entropy secrets; fast verify).</summary>
    internal const string Sha256StoredHashPrefix = "sha256:";

    private readonly TempoDbContext _db;
    private readonly PasswordService _passwordService;
    private readonly ApiKeySecretProtector _protector;

    public ApiKeyService(TempoDbContext db, PasswordService passwordService, ApiKeySecretProtector protector)
    {
        _db = db;
        _passwordService = passwordService;
        _protector = protector;
    }

    /// <summary>
    /// Creates a key unless the user already has an active one (returns null). The plaintext is stored only
    /// as a hash (for authentication) and as ciphertext (for <see cref="TryRevealAsync"/>).
    /// </summary>
    public async Task<(ApiKey Entity, string PlaintextKey)?> TryCreateAsync(Guid userId, string? label, CancellationToken cancellationToken = default)
    {
        if (await _db.ApiKeys.AnyAsync(k => k.UserId == userId && k.RevokedAt == null, cancellationToken))
        {
            return null;
        }

        var plaintextKey = GenerateSecret();
        var keyPrefix = BuildKeyPrefix(plaintextKey);
        var entity = new ApiKey
        {
            UserId = userId,
            Label = NormalizeLabel(label),
            KeyHash = HashApiKeySecret(plaintextKey),
            KeyPrefix = keyPrefix,
            KeyCiphertext = _protector.Encrypt(plaintextKey),
            CreatedAt = DateTime.UtcNow
        };

        _db.ApiKeys.Add(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsActiveKeyUniqueViolation(ex))
        {
            // Lost a race with a concurrent create; IX_ApiKeys_UserId_Active kept the invariant.
            _db.Entry(entity).State = EntityState.Detached;
            return null;
        }

        return (entity, plaintextKey);
    }

    /// <summary>
    /// Decrypts the user's active key <paramref name="apiKeyId"/>. Revoked or foreign keys are <see cref="ApiKeyRevealStatus.NotFound"/>.
    /// </summary>
    public async Task<(ApiKeyRevealStatus Status, string? PlaintextKey)> TryRevealAsync(
        Guid userId,
        Guid apiKeyId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(
                k => k.Id == apiKeyId && k.UserId == userId && k.RevokedAt == null,
                cancellationToken);

        if (entity == null)
        {
            return (ApiKeyRevealStatus.NotFound, null);
        }

        if (entity.KeyCiphertext == null)
        {
            return (ApiKeyRevealStatus.Unavailable, null);
        }

        string plaintextKey;
        try
        {
            plaintextKey = _protector.Decrypt(entity.KeyCiphertext);
        }
        catch (CryptographicException)
        {
            return (ApiKeyRevealStatus.Unavailable, null);
        }

        // Ciphertext must still match the hash that authenticates this key.
        if (!VerifyApiKeyAgainstStoredHash(plaintextKey, entity.KeyHash))
        {
            return (ApiKeyRevealStatus.Unavailable, null);
        }

        return (ApiKeyRevealStatus.Revealed, plaintextKey);
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
            if (VerifyApiKeyAgainstStoredHash(plaintextKey, k.KeyHash))
            {
                return k.User;
            }
        }

        return null;
    }

    /// <summary>
    /// High-entropy API keys use SHA-256 for storage (fast). Legacy rows may still use BCrypt (<c>$2…</c>).
    /// </summary>
    private bool VerifyApiKeyAgainstStoredHash(string plaintextKey, string storedHash)
    {
        if (storedHash.StartsWith('$'))
        {
            return _passwordService.VerifyPassword(plaintextKey, storedHash);
        }

        return VerifyApiKeySecretSha256(plaintextKey, storedHash);
    }

    private static string HashApiKeySecret(string plaintextKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey));
        return Sha256StoredHashPrefix + Convert.ToBase64String(hash);
    }

    private static bool VerifyApiKeySecretSha256(string plaintextKey, string storedHash)
    {
        if (!storedHash.StartsWith(Sha256StoredHashPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encoded = storedHash.Substring(Sha256StoredHashPrefix.Length);
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != SHA256.HashSizeInBytes)
        {
            return false;
        }

        Span<byte> computed = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey), computed);
        return CryptographicOperations.FixedTimeEquals(computed, expected);
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

    private static bool IsActiveKeyUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return pg.ConstraintName == "IX_ApiKeys_UserId_Active";
            }
        }

        return false;
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
