using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Helper class for creating authenticated HttpClient instances for testing
/// </summary>
public static class TestHttpClientFactory
{
    /// <summary>
    /// Semaphore to ensure thread-safe user creation in tests.
    /// Protects against race conditions where multiple tests might try to create the same user concurrently.
    /// </summary>
    private static readonly SemaphoreSlim _userCreationLock = new(1, 1);

    /// <summary>
    /// Creates an authenticated HttpClient by logging in with username and password
    /// </summary>
    /// <param name="factory">WebApplicationFactory instance</param>
    /// <param name="username">Username (default: "testuser")</param>
    /// <param name="password">Password (default: <see cref="TestPasswords.Default"/>)</param>
    /// <returns>HttpClient with authentication cookie set</returns>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string username = "testuser",
        string password = TestPasswords.Default)
    {
        var client = factory.CreateClient();

        // First, ensure database schema exists and user exists
        // Use semaphore to prevent race conditions when multiple tests try to create the same user concurrently
        await _userCreationLock.WaitAsync();
        try
        {
            using (var scope = factory.Server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
                
                // Ensure database schema is created (in case it wasn't created during host initialization)
                try
                {
                    await db.Database.EnsureCreatedAsync();
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    // Schema already exists, continue
                }
                
                var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
                
                if (user == null)
                {
                    // Create user if it doesn't exist
                    user = await TestDataSeeder.SeedUserAsync(db, username, password);
                }
            }
        }
        finally
        {
            _userCreationLock.Release();
        }

        // Login to get JWT token
        var loginRequest = new
        {
            username,
            password
        };

        var loginResponse = await client.PostAsJsonAsync("/auth/login", loginRequest);
        
        // The JWT token is set as a cookie by the login endpoint
        // HttpClient from WebApplicationFactory automatically handles cookies
        // Verify the login was successful
        if (!loginResponse.IsSuccessStatusCode)
        {
            var errorContent = await loginResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Failed to authenticate user '{username}'. Status: {loginResponse.StatusCode}. Content: {errorContent}");
        }

        return client;
    }

    /// <summary>
    /// Creates an authenticated HttpClient using an existing user
    /// </summary>
    /// <param name="factory">WebApplicationFactory instance</param>
    /// <param name="user">Existing User entity</param>
    /// <param name="password">Password for the user (required to login)</param>
    /// <returns>HttpClient with authentication cookie set</returns>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        User user,
        string password)
    {
        return await CreateAuthenticatedClientAsync(factory, user.Username, password);
    }


    /// <summary>
    /// Creates an unauthenticated HttpClient
    /// </summary>
    /// <param name="factory">WebApplicationFactory instance</param>
    /// <returns>HttpClient without authentication</returns>
    public static HttpClient CreateUnauthenticatedClient(WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient();
    }
}
