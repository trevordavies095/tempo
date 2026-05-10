namespace Tempo.Api.Authentication;

/// <summary>
/// Custom JWT claim types used by Tempo (in addition to standard <see cref="System.Security.Claims.ClaimTypes"/>).
/// </summary>
public static class TempoJwtClaimTypes
{
    public const string SessionVersion = "sess_ver";

    public const string RememberMe = "remember_me";
}
