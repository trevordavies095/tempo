using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Service for JWT token generation and validation
/// </summary>
public class JwtService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationDays;
    private readonly int _rememberMeExpirationDays;
    private readonly ILogger<JwtService> _logger;

    /// <summary>
    /// Gets the configured JWT token expiration in days
    /// </summary>
    public int ExpirationDays => _expirationDays;

    /// <summary>
    /// Gets the configured JWT token expiration in days for "Remember Me" sessions
    /// </summary>
    public int RememberMeExpirationDays => _rememberMeExpirationDays;

    public JwtService(IConfiguration configuration, ILogger<JwtService> logger)
    {
        _secretKey = configuration["JWT:SecretKey"] 
            ?? throw new InvalidOperationException("JWT:SecretKey is not configured");
        _issuer = configuration["JWT:Issuer"] ?? "Tempo";
        _audience = configuration["JWT:Audience"] ?? "Tempo";
        _expirationDays = configuration.GetValue<int>("JWT:ExpirationDays", 7);
        _rememberMeExpirationDays = configuration.GetValue<int>("JWT:RememberMeExpirationDays", 30);
        _logger = logger;
    }

    /// <summary>
    /// Generates a JWT token for a user
    /// </summary>
    /// <param name="user">User entity</param>
    /// <param name="expirationDays">Optional expiration in days. If not provided, uses default expiration.</param>
    /// <returns>JWT token string</returns>
    public string GenerateToken(User user, int? expirationDays = null)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiration = expirationDays ?? _expirationDays;

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(expiration),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Validates a JWT token and returns the claims principal
    /// </summary>
    /// <param name="token">JWT token string</param>
    /// <returns>ClaimsPrincipal if token is valid, null otherwise</returns>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWT token validation failed");
            return null;
        }
    }
}

