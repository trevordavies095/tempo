using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Authorization;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Utils;

namespace Tempo.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Register a new user account
    /// </summary>
    private static async Task<IResult> Register(
        RegisterRequest request,
        TempoDbContext db,
        PasswordService passwordService,
        ILogger<Program> logger)
    {
        // Validate username
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 50)
        {
            return Results.BadRequest(new { error = "Username must be between 1 and 50 characters" });
        }

        // Validate password
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            return Results.BadRequest(new { error = "Password must be at least 6 characters" });
        }

        // Use a serializable transaction to atomically check and create user
        // This prevents race conditions where multiple concurrent requests could both
        // pass the "no users exist" check before either commits
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            // Check if any users exist - if so, registration is locked
            var userExists = await db.Users.AnyAsync();
            if (userExists)
            {
                await transaction.RollbackAsync();
                return Results.BadRequest(new { error = "Registration is disabled. An account already exists." });
            }

            // Check if username already exists (trim before comparison)
            var trimmedUsername = request.Username.Trim();
            var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == trimmedUsername);
            if (existingUser != null)
            {
                await transaction.RollbackAsync();
                return Results.BadRequest(new { error = "Username already exists" });
            }

            // Create new user
            var user = new User
            {
                Username = trimmedUsername,
                PasswordHash = passwordService.HashPassword(request.Password),
                CreatedAt = DateTime.UtcNow
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            logger.LogInformation("User registered: {Username}", LogSanitizer.Sanitize(user.Username));

            return Results.Ok(new
            {
                message = "User registered successfully",
                userId = user.Id
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("serialization") == true || 
                                            ex.InnerException?.Message?.Contains("could not serialize") == true)
        {
            // Handle serialization failure (concurrent registration attempt)
            await transaction.RollbackAsync();
            logger.LogWarning("Registration failed due to concurrent attempt: {Username}", LogSanitizer.Sanitize(request.Username));
            return Results.BadRequest(new { error = "Registration is disabled. An account already exists." });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Login and receive JWT token
    /// </summary>
    private static async Task<IResult> Login(
        LoginRequest request,
        TempoDbContext db,
        PasswordService passwordService,
        JwtService jwtService,
        HttpContext httpContext,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "Username and password are required" });
        }

        // Find user (trim username before lookup to match how it's stored)
        var trimmedUsername = request.Username.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == trimmedUsername);
        if (user == null)
        {
            // Don't reveal if user exists or not
            return Results.Unauthorized();
        }

        // Verify password
        if (!passwordService.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Results.Unauthorized();
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Determine expiration days based on RememberMe flag
        var expirationDays = request.RememberMe 
            ? jwtService.RememberMeExpirationDays 
            : jwtService.ExpirationDays;

        // Generate token with appropriate expiration
        var token = jwtService.GenerateToken(user, expirationDays);

        var expirationDate = DateTime.UtcNow.AddDays(expirationDays);

        // Set httpOnly cookie with production-safe configuration
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            // In production, always use Secure=true (production should always use HTTPS)
            // In development, use Request.IsHttps to allow local development without HTTPS
            Secure = environment.IsProduction() ? true : httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expirationDate
        };

        // Optionally set cookie domain from configuration for cross-subdomain scenarios
        var cookieDomain = configuration["Cookie:Domain"];
        if (!string.IsNullOrWhiteSpace(cookieDomain))
        {
            cookieOptions.Domain = cookieDomain;
        }

        httpContext.Response.Cookies.Append("authToken", token, cookieOptions);

        logger.LogInformation("User logged in: {Username}", LogSanitizer.Sanitize(user.Username));

        return Results.Ok(new
        {
            userId = user.Id,
            username = user.Username,
            expiresAt = expirationDate
        });
    }

    /// <summary>
    /// Get current user info from JWT token
    /// </summary>
    private static async Task<IResult> GetCurrentUser(
        ClaimsPrincipal user,
        TempoDbContext db)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var dbUser = await db.Users.FindAsync(userId);
        if (dbUser == null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new
        {
            userId = dbUser.Id,
            username = dbUser.Username,
            createdAt = dbUser.CreatedAt,
            lastLoginAt = dbUser.LastLoginAt
        });
    }

    /// <summary>
    /// Logout (clear auth cookie)
    /// </summary>
    private static IResult Logout(
        HttpContext httpContext,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        // Must use the same cookie options as Login to properly delete the cookie
        // especially when Cookie:Domain is configured
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = environment.IsProduction() ? true : httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        };

        // Set the same domain as used during login
        var cookieDomain = configuration["Cookie:Domain"];
        if (!string.IsNullOrWhiteSpace(cookieDomain))
        {
            cookieOptions.Domain = cookieDomain;
        }

        httpContext.Response.Cookies.Delete("authToken", cookieOptions);
        return Results.Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Check if registration is available (no users exist)
    /// </summary>
    private static async Task<IResult> CheckRegistrationAvailable(TempoDbContext db)
    {
        var userExists = await db.Users.AnyAsync();
        return Results.Ok(new { registrationAvailable = !userExists });
    }

    private static Guid? GetUserIdFromClaims(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return userId;
    }

    private static async Task<IResult> CreateApiKey(
        CreateApiKeyRequest? request,
        ClaimsPrincipal user,
        ApiKeyService apiKeyService,
        CancellationToken cancellationToken)
    {
        var userId = GetUserIdFromClaims(user);
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        var label = request?.Label;
        if (label != null && label.Length > 200)
        {
            return Results.BadRequest(new { error = "Label must be at most 200 characters" });
        }

        var (entity, plaintextKey) = await apiKeyService.CreateAsync(userId.Value, label, cancellationToken);

        return TypedResults.Json(
            new
            {
                id = entity.Id,
                label = entity.Label,
                key = plaintextKey,
                keyPrefix = entity.KeyPrefix,
                createdAt = entity.CreatedAt
            },
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListApiKeys(
        ClaimsPrincipal user,
        ApiKeyService apiKeyService,
        CancellationToken cancellationToken)
    {
        var userId = GetUserIdFromClaims(user);
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        var keys = await apiKeyService.ListForUserAsync(userId.Value, cancellationToken);
        return Results.Ok(keys.Select(k => new
        {
            id = k.Id,
            label = k.Label,
            keyPrefix = k.KeyPrefix,
            createdAt = k.CreatedAt,
            revokedAt = k.RevokedAt
        }));
    }

    private static async Task<IResult> RevokeApiKey(
        Guid id,
        ClaimsPrincipal user,
        ApiKeyService apiKeyService,
        CancellationToken cancellationToken)
    {
        var userId = GetUserIdFromClaims(user);
        if (userId == null)
        {
            return Results.Unauthorized();
        }

        var revoked = await apiKeyService.TryRevokeAsync(userId.Value, id, cancellationToken);
        if (!revoked)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth")
            .WithTags("Authentication");

        group.MapPost("/register", Register)
            .WithName("Register")
            .Produces(200)
            .Produces(400)
            .WithSummary("Register new user")
            .WithDescription("Creates a new user account. Registration is only available if no users exist in the system.");

        group.MapPost("/login", Login)
            .WithName("Login")
            .Produces(200)
            .Produces(401)
            .WithSummary("Login")
            .WithDescription("Authenticates user and returns JWT token in httpOnly cookie.");

        group.MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .RequireAuthorization()
            .Produces(200)
            .Produces(401)
            .WithSummary("Get current user")
            .WithDescription("Returns information about the currently authenticated user.");

        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .Produces(200)
            .WithSummary("Logout")
            .WithDescription("Clears the authentication cookie.");

        group.MapGet("/registration-available", CheckRegistrationAvailable)
            .WithName("CheckRegistrationAvailable")
            .Produces(200)
            .WithSummary("Check if registration is available")
            .WithDescription("Returns whether registration is available (true if no users exist).");

        group.MapPost("/api-keys", CreateApiKey)
            .WithName("CreateApiKey")
            .RequireAuthorization(AuthorizationPolicyNames.JwtSessionOnly)
            .Produces(StatusCodes.Status201Created)
            .Produces(400)
            .Produces(401)
            .WithSummary("Create API key")
            .WithDescription(
                "Creates an API key for machine/CLI access. The full key is returned once; store it securely. Requires a browser JWT session (not an API key).");

        group.MapGet("/api-keys", ListApiKeys)
            .WithName("ListApiKeys")
            .RequireAuthorization(AuthorizationPolicyNames.JwtSessionOnly)
            .Produces(200)
            .Produces(401)
            .WithSummary("List API keys")
            .WithDescription("Returns metadata for your API keys (never the secret value).");

        group.MapDelete("/api-keys/{id:guid}", RevokeApiKey)
            .WithName("RevokeApiKey")
            .RequireAuthorization(AuthorizationPolicyNames.JwtSessionOnly)
            .Produces(204)
            .Produces(401)
            .Produces(404)
            .WithSummary("Revoke API key")
            .WithDescription("Soft-revokes an API key so it no longer authenticates.");
    }

    /// <summary>
    /// Request model for user registration
    /// </summary>
    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for user login
    /// </summary>
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; } = false;
    }

    public class CreateApiKeyRequest
    {
        public string? Label { get; set; }
    }
}

