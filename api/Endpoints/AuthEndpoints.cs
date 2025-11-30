using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

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
        // Check if any users exist - if so, registration is locked
        var userExists = await db.Users.AnyAsync();
        if (userExists)
        {
            return Results.BadRequest(new { error = "Registration is disabled. An account already exists." });
        }

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

        // Check if username already exists
        var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (existingUser != null)
        {
            return Results.BadRequest(new { error = "Username already exists" });
        }

        // Create new user
        var user = new User
        {
            Username = request.Username.Trim(),
            PasswordHash = passwordService.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        logger.LogInformation("User registered: {Username}", user.Username);

        return Results.Ok(new
        {
            message = "User registered successfully",
            userId = user.Id
        });
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
        ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "Username and password are required" });
        }

        // Find user
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
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

        // Generate token
        var token = jwtService.GenerateToken(user);

        // Set httpOnly cookie
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTime.UtcNow.AddDays(7)
        };
        httpContext.Response.Cookies.Append("authToken", token, cookieOptions);

        logger.LogInformation("User logged in: {Username}", user.Username);

        return Results.Ok(new
        {
            userId = user.Id,
            username = user.Username,
            expiresAt = DateTime.UtcNow.AddDays(7)
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
    private static IResult Logout(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete("authToken");
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
    }
}

