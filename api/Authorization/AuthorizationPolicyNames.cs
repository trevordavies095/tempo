namespace Tempo.Api.Authorization;

/// <summary>
/// Authorization policy names.
/// </summary>
public static class AuthorizationPolicyNames
{
    /// <summary>
    /// Requires a real JWT session (interactive login). API-key principals built in Theme C must
    /// omit the <c>jti</c> claim so they cannot manage API keys; reuse <see cref="Services.ApiKeyService.TryGetActiveUserIdAsync"/> for key validation.
    /// </summary>
    public const string JwtSessionOnly = "JwtSessionOnly";
}
