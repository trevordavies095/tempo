namespace Tempo.Api.Authentication;

/// <summary>
/// Host config for the <c>authToken</c> session cookie. Read once at startup; restart to change.
/// </summary>
/// <param name="SecureOverride">
/// <c>Cookie:Secure</c>. Null = default (always Secure in Production, otherwise follow the
/// request scheme after trusted forwarded headers). True/false forces the flag.
/// </param>
/// <param name="Domain"><c>Cookie:Domain</c>. Null = host-only cookie (recommended).</param>
public sealed record AuthCookieSettings(bool? SecureOverride, string? Domain);

/// <summary>
/// Builds the session cookie options shared by login, password change, and logout so the
/// delete in logout always matches the cookie that was set.
/// </summary>
public static class AuthCookie
{
    public const string Name = "authToken";
    public const string SecureConfigKey = "Cookie:Secure";
    public const string DomainConfigKey = "Cookie:Domain";

    /// <summary>
    /// Reads <c>Cookie:Secure</c> and <c>Cookie:Domain</c>. Unknown <c>Cookie:Secure</c> values
    /// throw before the host listens so a typo can't silently weaken or break login.
    /// </summary>
    public static AuthCookieSettings ReadSettings(IConfiguration configuration)
    {
        var domain = configuration[DomainConfigKey];
        return new AuthCookieSettings(
            ParseSecure(configuration[SecureConfigKey]),
            string.IsNullOrWhiteSpace(domain) ? null : domain.Trim());
    }

    public static bool? ParseSecure(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value.Trim(), out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Unknown {SecureConfigKey} '{value.Trim()}'. Allowed values: true, false, or unset.");
    }

    /// <summary>
    /// Whether the cookie gets the <c>Secure</c> flag. Production defaults to always Secure;
    /// only an explicit <c>Cookie:Secure=false</c> turns it off there.
    /// </summary>
    public static bool ResolveSecure(AuthCookieSettings settings, bool isProduction, bool requestIsHttps) =>
        settings.SecureOverride ?? (isProduction || requestIsHttps);

    public static CookieOptions BuildOptions(
        HttpContext httpContext,
        AuthCookieSettings settings,
        IWebHostEnvironment environment,
        DateTime? expiresUtc = null)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = ResolveSecure(settings, environment.IsProduction(), httpContext.Request.IsHttps),
            SameSite = SameSiteMode.Lax,
            Path = "/",
        };

        if (expiresUtc is not null)
        {
            options.Expires = expiresUtc;
        }

        if (settings.Domain is not null)
        {
            options.Domain = settings.Domain;
        }

        return options;
    }

    /// <summary>
    /// True when a Secure cookie is about to be sent on a plain-HTTP request from a non-localhost
    /// host. Browsers drop it, so login "succeeds" and the next request is signed out.
    /// Localhost is exempt: browsers accept Secure cookies on http://localhost.
    /// </summary>
    public static bool WillBrowserDropSecureCookie(HttpContext httpContext, bool secure)
    {
        if (!secure || httpContext.Request.IsHttps)
        {
            return false;
        }

        // The command center reaches the API through Next's /api rewrite, which sets
        // X-Forwarded-Host to the browser's host. Used only for this diagnostic.
        var forwardedHost = httpContext.Request.Headers["X-Forwarded-Host"].ToString();
        var host = string.IsNullOrWhiteSpace(forwardedHost)
            ? httpContext.Request.Host.Host
            : forwardedHost.Split(',')[0].Trim();

        return !IsLocalhost(host);
    }

    private static bool IsLocalhost(string host)
    {
        // Strip a port ("localhost:3000") or IPv6 brackets ("[::1]:3000").
        var bare = host.StartsWith('[')
            ? host[1..Math.Max(1, host.IndexOf(']'))]
            : host.Split(':')[0];

        return string.Equals(bare, "localhost", StringComparison.OrdinalIgnoreCase)
            || bare.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || bare == "127.0.0.1"
            || bare == "::1";
    }
}
