using System.Security.Cryptography;
using System.Text;

namespace Tempo.Api.Services;

/// <summary>
/// AES-GCM encrypt/decrypt for the intervals.icu API key.
/// Wrapping key is HKDF-SHA256 of JWT:SecretKey with a purpose string.
/// </summary>
public sealed class IntervalsIcuSecretProtector
{
    public const string Purpose = "tempo-intervals-icu-api-key";

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly byte[] _key;

    public IntervalsIcuSecretProtector(IConfiguration configuration)
    {
        var secret = configuration["JWT:SecretKey"]
            ?? throw new InvalidOperationException("JWT:SecretKey is not configured");

        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(secret),
            outputLength: KeySize,
            salt: null,
            info: Encoding.UTF8.GetBytes(Purpose));
    }

    public byte[] Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var blob = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, blob, NonceSize + TagSize, ciphertext.Length);
        return blob;
    }

    public string Decrypt(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        if (blob.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Ciphertext is too short.");
        }

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var ciphertext = blob.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
