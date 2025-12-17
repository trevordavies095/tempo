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
    /// Creates an authenticated HttpClient by logging in with username and password
    /// </summary>
    /// <param name="factory">WebApplicationFactory instance</param>
    /// <param name="username">Username (default: "testuser")</param>
    /// <param name="password">Password (default: "Test123!")</param>
    /// <returns>HttpClient with authentication cookie set</returns>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string username = "testuser",
        string password = "Test123!")
    {
        var client = factory.CreateClient();

        // First, ensure user exists
        using (var scope = factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            
            if (user == null)
            {
                // Create user if it doesn't exist
                user = await TestDataSeeder.SeedUserAsync(db, username, password);
            }
        }

        // Login to get JWT token
        var loginRequest = new
        {
            username,
            password
        };

        var loginResponse = await client.PostAsJsonAsync("/auth/login", loginRequest);
        
        if (!loginResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to authenticate user '{username}'. Status: {loginResponse.StatusCode}");
        }

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
