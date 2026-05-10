namespace Tempo.Api.Authentication;

/// <summary>
/// Shared auth-failure text for Theme C (JWT Bearer challenge and API key challenge).
/// </summary>
public static class AuthErrorMessages
{
    /// <summary>Generic 401 message; avoids distinguishing invalid JWT vs revoked key.</summary>
    public const string Unauthorized = "Invalid or expired credentials";
}
