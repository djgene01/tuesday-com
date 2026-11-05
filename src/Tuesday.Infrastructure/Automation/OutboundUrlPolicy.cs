using System.Net;
using System.Net.Sockets;

namespace Tuesday.Infrastructure.Automations;

/// <summary>
/// Refuses outbound webhook targets that point inside the deployment's own network.
///
/// Registration is only half of it: the address is checked again immediately before every
/// delivery, because DNS can be re-pointed after a URL has been registered (rebinding). The
/// residual gap is that the check and the socket connect are separate operations; closing it
/// entirely needs a connect-time resolver hook, which is why this is documented rather than
/// implied.
/// </summary>
public static class OutboundUrlPolicy
{
    private static readonly (IPAddress Network, int Prefix)[] BlockedNetworks =
    [
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("169.254.0.0"), 16),   // link-local, including cloud metadata
        (IPAddress.Parse("100.64.0.0"), 10),    // carrier-grade NAT
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("198.18.0.0"), 15),    // benchmarking range
        (IPAddress.Parse("::1"), 128),
        (IPAddress.Parse("fc00::"), 7),         // unique local
        (IPAddress.Parse("fe80::"), 10),        // link-local
        (IPAddress.Parse("ff00::"), 8)          // multicast
    ];

    /// <summary>Returns null when the URL is allowed, otherwise the reason it is not.</summary>
    public static async Task<string?> ValidateAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "A webhook needs a URL.";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "A webhook needs an absolute URL.";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return "A webhook URL must use http or https.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "A webhook URL must not carry credentials.";
        }

        if (uri.IsLoopback)
        {
            return "A webhook URL must not point at this machine.";
        }

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            return IsPublic(literal) ? null : Blocked(literal);
        }

        IPAddress[] addresses;

        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return $"The webhook host '{uri.Host}' could not be resolved.";
        }

        if (addresses.Length == 0)
        {
            return $"The webhook host '{uri.Host}' resolved to no addresses.";
        }

        foreach (var address in addresses)
        {
            if (!IsPublic(address))
            {
                return Blocked(address);
            }
        }

        return null;
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6
            && (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast))
        {
            return false;
        }

        return !InBlockedNetwork(address);
    }

    private static bool InBlockedNetwork(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        foreach (var (network, prefix) in BlockedNetworks)
        {
            var networkBytes = network.GetAddressBytes();

            if (networkBytes.Length != bytes.Length)
            {
                continue;
            }

            if (Matches(bytes, networkBytes, prefix))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(byte[] value, byte[] network, int prefix)
    {
        var fullBytes = prefix / 8;
        var remainingBits = prefix % 8;

        for (var index = 0; index < fullBytes; index++)
        {
            if (value[index] != network[index])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));

        return (value[fullBytes] & mask) == (network[fullBytes] & mask);
    }

    private static string Blocked(IPAddress address) =>
        $"A webhook URL must not point into a private or link-local network ({address}).";
}
