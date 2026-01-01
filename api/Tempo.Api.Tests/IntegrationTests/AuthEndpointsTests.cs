using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for AuthEndpoints covering registration, login, logout, and user info endpoints
/// </summary>
[Collection("Integration Tests")]
public class AuthEndpointsTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public AuthEndpointsTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Helper method to ensure database is clean before a test (removes all users)
    /// </summary>
    private async Task EnsureCleanDatabaseAsync()
    {
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Clear all users to allow registration
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Users");
            await db.SaveChangesAsync();
        }
    }

    #region Register Endpoint Tests

    [Fact]
    public async Task Register_WithValidCredentials_ReturnsSuccess()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var request = new
        {
            username = "newuser",
            password = "Password123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        result.Should().NotBeNull();
        result!.Message.Should().Contain("successfully");
        result.UserId.Should().NotBeEmpty();

        // Verify user was created
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "newuser");
            user.Should().NotBeNull();
            user!.Username.Should().Be("newuser");
        }
    }

    [Fact]
    public async Task Register_WithEmptyUsername_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var request = new
        {
            username = "",
            password = "Password123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        result.Should().NotBeNull();
        result!.Error.Should().Contain("Username");
    }

    [Fact]
    public async Task Register_WithUsernameTooLong_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var request = new
        {
            username = new string('a', 51), // 51 characters
            password = "Password123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        result.Should().NotBeNull();
        result!.Error.Should().Contain("Username");
    }

    [Fact]
    public async Task Register_WithPasswordTooShort_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var request = new
        {
            username = "newuser",
            password = "12345" // Less than 6 characters
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        result.Should().NotBeNull();
        result!.Error.Should().Contain("Password");
    }

    [Fact]
    public async Task Register_WithEmptyPassword_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var request = new
        {
            username = "newuser",
            password = ""
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        result.Should().NotBeNull();
        result!.Error.Should().Contain("Password");
    }

    [Fact]
    public async Task Register_WithWhitespaceUsername_TrimsAndCreatesUser()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var request = new
        {
            username = "  newuser  ",
            password = "Password123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify username was trimmed
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "newuser");
            user.Should().NotBeNull();
            user!.Username.Should().Be("newuser"); // Should be trimmed
        }
    }

    [Fact]
    public async Task Register_WhenUserAlreadyExists_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Create first user
        var firstRequest = new
        {
            username = "existinguser",
            password = "Password123!"
        };
        await client.PostAsJsonAsync("/auth/register", firstRequest);

        // Try to register same username
        var secondRequest = new
        {
            username = "existinguser",
            password = "Password456!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/register", secondRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        result.Should().NotBeNull();
        result!.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Register_WhenRegistrationIsLocked_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Create first user to lock registration
        var firstRequest = new
        {
            username = "firstuser",
            password = "Password123!"
        };
        await client.PostAsJsonAsync("/auth/register", firstRequest);

        // Try to register another user
        var secondRequest = new
        {
            username = "seconduser",
            password = "Password456!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/register", secondRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        result.Should().NotBeNull();
        result!.Error.Should().Contain("Registration is disabled");
    }

    #endregion

    #region Login Endpoint Tests

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsSuccess()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Register user first
        var registerRequest = new
        {
            username = "testuser",
            password = "Password123!"
        };
        await client.PostAsJsonAsync("/auth/register", registerRequest);

        var loginRequest = new
        {
            username = "testuser",
            password = "Password123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        result.Should().NotBeNull();
        result!.UserId.Should().NotBeEmpty();
        result.Username.Should().Be("testuser");
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        // Verify cookie was set
        var cookies = response.Headers.GetValues("Set-Cookie");
        cookies.Should().Contain(c => c.Contains("authToken"));
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Register user first
        var registerRequest = new
        {
            username = "testuser",
            password = "Password123!"
        };
        await client.PostAsJsonAsync("/auth/register", registerRequest);

        var loginRequest = new
        {
            username = "testuser",
            password = "WrongPassword"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonExistentUser_ReturnsUnauthorized()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var loginRequest = new
        {
            username = "nonexistent",
            password = "Password123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithEmptyUsername_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var loginRequest = new
        {
            username = "",
            password = "Password123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        result.Should().NotBeNull();
        result!.Error.Should().Contain("required");
    }

    [Fact]
    public async Task Login_WithEmptyPassword_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var loginRequest = new
        {
            username = "testuser",
            password = ""
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        result.Should().NotBeNull();
        result!.Error.Should().Contain("required");
    }

    [Fact]
    public async Task Login_WithWhitespaceUsername_TrimsAndLogsIn()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Register user
        var registerRequest = new
        {
            username = "testuser",
            password = "Password123!"
        };
        await client.PostAsJsonAsync("/auth/register", registerRequest);

        // Login with whitespace
        var loginRequest = new
        {
            username = "  testuser  ",
            password = "Password123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_UpdatesLastLoginAt()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Register and login first time
        var registerRequest = new
        {
            username = "testuser",
            password = "Password123!"
        };
        await client.PostAsJsonAsync("/auth/register", registerRequest);

        await Task.Delay(100); // Small delay to ensure different timestamp

        var loginRequest = new
        {
            username = "testuser",
            password = "Password123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == "testuser");
            user.Should().NotBeNull();
            user!.LastLoginAt.Should().NotBeNull();
            user.LastLoginAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }
    }

    #endregion

    #region GetCurrentUser Endpoint Tests

    [Fact]
    public async Task GetCurrentUser_WhenAuthenticated_ReturnsUserInfo()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "testuser", "Test123!");

        // Act
        var response = await client.GetAsync("/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        result.Should().NotBeNull();
        result!.UserId.Should().NotBeEmpty();
        result.Username.Should().Be("testuser");
        result.CreatedAt.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public async Task GetCurrentUser_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrentUser_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();
        
        // Set invalid cookie
        client.DefaultRequestHeaders.Add("Cookie", "authToken=invalid-token");

        // Act
        var response = await client.GetAsync("/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrentUser_WithDeletedUser_ReturnsUnauthorized()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "testuser", "Test123!");
        
        // Get user ID from the authenticated response
        var initialResponse = await client.GetAsync("/auth/me");
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var userInfo = await initialResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        var userId = userInfo!.UserId;
        
        // Delete the user from the database
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var user = await db.Users.FindAsync(userId);
            if (user != null)
            {
                db.Users.Remove(user);
                await db.SaveChangesAsync();
            }
        }
        
        // Act - try to access /auth/me with valid JWT but deleted user
        var response = await client.GetAsync("/auth/me");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Logout Endpoint Tests

    [Fact]
    public async Task Logout_WhenAuthenticated_ClearsCookie()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "testuser", "Test123!");

        // Act
        var response = await client.PostAsync("/auth/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LogoutResponse>();
        result.Should().NotBeNull();
        result!.Message.Should().Contain("successfully");

        // Verify cookie was cleared (expired)
        var cookies = response.Headers.GetValues("Set-Cookie");
        cookies.Should().Contain(c => c.Contains("authToken") && c.Contains("expires="));
    }

    [Fact]
    public async Task Logout_WhenUnauthenticated_StillReturnsSuccess()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/auth/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region CheckRegistrationAvailable Endpoint Tests

    [Fact]
    public async Task CheckRegistrationAvailable_WhenNoUsersExist_ReturnsTrue()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/auth/registration-available");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RegistrationAvailableResponse>();
        result.Should().NotBeNull();
        result!.RegistrationAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRegistrationAvailable_WhenUserExists_ReturnsFalse()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        // Register a user
        var registerRequest = new
        {
            username = "testuser",
            password = "Password123!"
        };
        await client.PostAsJsonAsync("/auth/register", registerRequest);

        // Act
        var response = await client.GetAsync("/auth/registration-available");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RegistrationAvailableResponse>();
        result.Should().NotBeNull();
        result!.RegistrationAvailable.Should().BeFalse();
    }

    #endregion

    #region Helper Classes

    private class RegisterResponse
    {
        public string Message { get; set; } = string.Empty;
        public Guid UserId { get; set; }
    }

    private class LoginResponse
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    private class CurrentUserResponse
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    private class LogoutResponse
    {
        public string Message { get; set; } = string.Empty;
    }

    private class RegistrationAvailableResponse
    {
        public bool RegistrationAvailable { get; set; }
    }

    private class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
    }

    #endregion
}

