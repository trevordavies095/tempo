namespace Tempo.Api.Authentication;

public static class AuthenticationSchemes
{
    /// <summary>Policy scheme: forwards to JWT Bearer or API key based on <c>Authorization</c>.</summary>
    public const string TempoAuthentication = "TempoAuthentication";

    /// <summary>Machine credentials: <c>Bearer tmp_…</c> validated via <see cref="Services.ApiKeyService"/>.</summary>
    public const string ApiKey = "ApiKey";
}
