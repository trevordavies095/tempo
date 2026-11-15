using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Session cookie Secure flag behind a reverse proxy (#220). Runs in the Testing environment,
/// where an unset Cookie:Secure follows the request scheme, so X-Forwarded-Proto is observable.
/// Production's always-Secure default is covered by AuthCookieTests.ResolveSecure.
/// </summary>
[Collection("Integration Tests")]
public class ProxyCookieTests
{
    [Fact]
    public async Task Login_PlainHttp_CookieIsNotSecure()
    {
        using var factory = new TempoWebApplicationFactory();

        var cookie = await LoginAndGetCookieAsync(factory);

        cookie.Should().NotContainEquivalentOf("secure");
    }

    [Fact]
    public async Task Login_ForwardedProtoHttps_FromTrustedProxy_CookieIsSecure()
    {
        using var factory = new TempoWebApplicationFactory();

        var cookie = await LoginAndGetCookieAsync(factory, forwardedProto: "https");

        cookie.Should().ContainEquivalentOf("secure");
        cookie.Should().ContainEquivalentOf("httponly");
        cookie.Should().ContainEquivalentOf("samesite=lax");
    }

    [Fact]
    public async Task Login_ForwardedProtoHttps_FromUntrustedPeer_IsIgnored()
    {
        using var baseFactory = new TempoWebApplicationFactory();
        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tempo:Proxy:TrustedNetworks", "10.99.0.0/16");
            builder.ConfigureServices(services =>
                services.AddTransient<IStartupFilter>(_ => new RemoteIpStartupFilter(IPAddress.Parse("203.0.113.9"))));
        });

        var cookie = await LoginAndGetCookieAsync(factory, forwardedProto: "https");

        cookie.Should().NotContainEquivalentOf("secure");
    }

    [Fact]
    public async Task Login_CookieSecureTrue_IsSecureOnPlainHttp()
    {
        using var baseFactory = new TempoWebApplicationFactory();
        var factory = baseFactory.WithWebHostBuilder(builder => builder.UseSetting("Cookie:Secure", "true"));

        var cookie = await LoginAndGetCookieAsync(factory);

        cookie.Should().ContainEquivalentOf("secure");
    }

    [Fact]
    public async Task Logout_UsesSameSecureAndDomainAsLogin()
    {
        using var baseFactory = new TempoWebApplicationFactory();
        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Cookie:Secure", "true");
            builder.UseSetting("Cookie:Domain", "tempo.example.com");
        });
        // Read raw Set-Cookie; the client's cookie jar rejects a foreign Domain
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsync("/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("authToken="));
        cookie.Should().ContainEquivalentOf("secure");
        cookie.Should().ContainEquivalentOf("domain=tempo.example.com");
        cookie.Should().ContainEquivalentOf("expires=");
    }

    private static async Task<string> LoginAndGetCookieAsync(
        WebApplicationFactory<Program> factory,
        string? forwardedProto = null)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: false);
        }

        var client = factory.CreateClient();
        var credentials = new { username = "proxyuser", password = TestPasswords.Default };
        (await client.PostAsJsonAsync("/auth/register", credentials)).EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(credentials),
        };
        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("authToken="));
    }

    /// <summary>TestServer has no peer address; pin one ahead of the app pipeline.</summary>
    private sealed class RemoteIpStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
