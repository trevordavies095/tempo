using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Tempo.Api.Authentication;

/// <summary>
/// Which peers may set <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c>.
/// The command center reaches the API through Next's <c>/api</c> rewrite from the Compose
/// network, so the default trusts loopback and private ranges. Headers from any other
/// peer are ignored.
/// </summary>
public static class TrustedProxies
{
    public const string ConfigKey = "Tempo:Proxy:TrustedNetworks";

    public const string DefaultNetworks =
        "127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,fc00::/7";

    /// <summary>
    /// Parses a comma-separated CIDR list. Null/empty → <see cref="DefaultNetworks"/>.
    /// Invalid entries throw before the host listens.
    /// </summary>
    public static IReadOnlyList<System.Net.IPNetwork> Parse(string? value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? DefaultNetworks : value;
        var networks = new List<System.Net.IPNetwork>();

        foreach (var entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!System.Net.IPNetwork.TryParse(entry, out var network))
            {
                throw new InvalidOperationException(
                    $"Invalid {ConfigKey} entry '{entry}'. Use comma-separated CIDR ranges, e.g. 172.16.0.0/12,127.0.0.1/32.");
            }

            networks.Add(network);
        }

        return networks;
    }

    /// <summary>
    /// One forwarded hop (the edge proxy's values, passed through Next unchanged). Replaces
    /// ASP.NET's loopback-only defaults with <paramref name="networks"/>.
    /// </summary>
    public static void Configure(ForwardedHeadersOptions options, IEnumerable<System.Net.IPNetwork> networks)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }
}
