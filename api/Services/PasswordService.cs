using BCrypt.Net;

namespace Tempo.Api.Services;

/// <summary>
/// Service for password hashing and verification using BCrypt
/// </summary>
public class PasswordService
{
    private const int WorkFactor = 12;

    /// <summary>
    /// Hashes a password using BCrypt
    /// </summary>
    /// <param name="password">Plain text password</param>
    /// <returns>BCrypt hash string</returns>
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    /// <summary>
    /// Verifies a password against a BCrypt hash
    /// </summary>
    /// <param name="password">Plain text password</param>
    /// <param name="hash">BCrypt hash string</param>
    /// <returns>True if password matches hash, false otherwise</returns>
    public bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}

