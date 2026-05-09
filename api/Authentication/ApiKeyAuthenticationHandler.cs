using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tempo.Api.Services;

namespace Tempo.Api.Authentication;

/// <summary>
/// Validates <c>Authorization: Bearer tmp_…</c> via <see cref="ApiKeyService"/>.
/// Principals include <see cref="ClaimTypes.NameIdentifier"/> and <see cref="ClaimTypes.Name"/> but not <c>jti</c>,
/// so <c>JwtSessionOnly</c> routes stay browser-session-only.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authValues))
        {
            return AuthenticateResult.NoResult();
        }

        var auth = authValues.ToString();
        if (string.IsNullOrEmpty(auth) ||
            !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = auth["Bearer ".Length..].Trim();
        if (!token.StartsWith(ApiKeyService.KeyMaterialPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKeyService = Context.RequestServices.GetRequiredService<ApiKeyService>();
        var user = await apiKeyService.TryAuthenticateUserAsync(token, Context.RequestAborted);
        if (user == null)
        {
            return AuthenticateResult.Fail("API key validation failed");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        };
        var identity = new ClaimsIdentity(claims, AuthenticationSchemes.ApiKey);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthenticationSchemes.ApiKey);
        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        await Response.WriteAsJsonAsync(new { error = AuthErrorMessages.Unauthorized });
    }
}
