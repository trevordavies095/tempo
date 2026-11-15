using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Tempo.Api.Authentication;
using Xunit;

namespace Tempo.Api.Tests.Authentication;

public class AuthCookieTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData(" false ", false)]
    [InlineData("FALSE", false)]
    public void ParseSecure_AcceptsUnsetOrBool(string? value, bool? expected)
    {
        AuthCookie.ParseSecure(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("0")]
    [InlineData("yes")]
    public void ParseSecure_UnknownValue_Throws(string value)
    {
        var act = () => AuthCookie.ParseSecure(value);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cookie:Secure*");
    }

    [Theory]
    // Unset: Production always Secure, even on a plain-HTTP request
    [InlineData(null, true, false, true)]
    [InlineData(null, true, true, true)]
    // Unset outside Production: follow the (forwarded-aware) request scheme
    [InlineData(null, false, false, false)]
    [InlineData(null, false, true, true)]
    // Explicit override wins in every environment
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, true)]
    public void ResolveSecure(bool? overrideValue, bool isProduction, bool requestIsHttps, bool expected)
    {
        var settings = new AuthCookieSettings(overrideValue, null);
        AuthCookie.ResolveSecure(settings, isProduction, requestIsHttps).Should().Be(expected);
    }

    [Fact]
    public void ReadSettings_TrimsDomain_AndTreatsBlankAsHostOnly()
    {
        var withDomain = AuthCookie.ReadSettings(Config(("Cookie:Domain", " tempo.example.com ")));
        withDomain.Domain.Should().Be("tempo.example.com");
        withDomain.SecureOverride.Should().BeNull();

        var blank = AuthCookie.ReadSettings(Config(("Cookie:Domain", " "), ("Cookie:Secure", "false")));
        blank.Domain.Should().BeNull();
        blank.SecureOverride.Should().BeFalse();
    }

    [Theory]
    [InlineData("tempo.lan", null, true)]
    [InlineData("192.168.1.20:3004", null, true)]
    [InlineData("api:5001", "192.168.1.20:3004", true)]
    [InlineData("api:5001", "localhost:3000", false)]
    [InlineData("localhost:5001", null, false)]
    [InlineData("127.0.0.1:3000", null, false)]
    [InlineData("[::1]:3000", null, false)]
    [InlineData("tempo.localhost", null, false)]
    public void WillBrowserDropSecureCookie_OnPlainHttp(string host, string? forwardedHost, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = HostString.FromUriComponent(host);
        if (forwardedHost is not null)
        {
            context.Request.Headers["X-Forwarded-Host"] = forwardedHost;
        }

        AuthCookie.WillBrowserDropSecureCookie(context, secure: true).Should().Be(expected);
    }

    [Fact]
    public void WillBrowserDropSecureCookie_FalseOverHttpsOrWhenNotSecure()
    {
        var https = new DefaultHttpContext();
        https.Request.Scheme = "https";
        https.Request.Host = new HostString("tempo.example.com");
        AuthCookie.WillBrowserDropSecureCookie(https, secure: true).Should().BeFalse();

        var http = new DefaultHttpContext();
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("tempo.lan");
        AuthCookie.WillBrowserDropSecureCookie(http, secure: false).Should().BeFalse();
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}

public class TrustedProxiesTests
{
    [Fact]
    public void Parse_Default_TrustsLoopbackAndPrivateRanges()
    {
        var networks = TrustedProxies.Parse(null);

        networks.Should().Contain(n => n.Contains(IPAddress.Parse("127.0.0.1")));
        networks.Should().Contain(n => n.Contains(IPAddress.Parse("172.18.0.3")));
        networks.Should().Contain(n => n.Contains(IPAddress.Parse("10.0.0.5")));
        networks.Should().Contain(n => n.Contains(IPAddress.Parse("192.168.4.2")));
        networks.Should().Contain(n => n.Contains(IPAddress.IPv6Loopback));
        networks.Should().NotContain(n => n.Contains(IPAddress.Parse("203.0.113.9")));
    }

    [Fact]
    public void Parse_CustomList_ReplacesDefaults()
    {
        var networks = TrustedProxies.Parse(" 172.20.0.0/16 , 127.0.0.1/32 ");

        networks.Should().HaveCount(2);
        networks.Should().NotContain(n => n.Contains(IPAddress.Parse("10.0.0.5")));
    }

    [Theory]
    [InlineData("172.20.0.0")]
    [InlineData("not-a-cidr")]
    [InlineData("10.0.0.0/33")]
    public void Parse_InvalidEntry_Throws(string value)
    {
        var act = () => TrustedProxies.Parse(value);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Tempo:Proxy:TrustedNetworks*");
    }

    [Fact]
    public void Configure_ReplacesLoopbackOnlyDefaults_WithOneHop()
    {
        var options = new ForwardedHeadersOptions();
        TrustedProxies.Configure(options, TrustedProxies.Parse("172.20.0.0/16"));

        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.ForwardLimit.Should().Be(1);
        options.KnownProxies.Should().BeEmpty();
        options.KnownIPNetworks.Should().ContainSingle();
    }
}
