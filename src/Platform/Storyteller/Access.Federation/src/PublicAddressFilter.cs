using System.Net;
using System.Net.Sockets;

namespace _42.Platform.Storyteller;

// Whether issuer metadata may be fetched from an address: only addresses the IANA IPv4 and IPv6 Special-Purpose
// Address Registries mark globally reachable, so a trusted issuer cannot point Storyteller at its own network
// (loopback, private ranges, the cloud metadata endpoint 169.254.169.254). IPv4 embedded in IPv6 is checked as IPv4.
public static class PublicAddressFilter
{
    // Not globally reachable (RFC 6890 and its updates), plus multicast and the deprecated 6to4 relay anycast.
    private static readonly (uint Network, int PrefixLength)[] NonPublicIPv4 =
    [
        (Ipv4(0, 0, 0, 0), 8),         // "this network"
        (Ipv4(10, 0, 0, 0), 8),        // private
        (Ipv4(100, 64, 0, 0), 10),     // shared address space (CGNAT)
        (Ipv4(127, 0, 0, 0), 8),       // loopback
        (Ipv4(169, 254, 0, 0), 16),    // link local, cloud metadata endpoints
        (Ipv4(172, 16, 0, 0), 12),     // private
        (Ipv4(192, 0, 0, 0), 24),      // IETF protocol assignments
        (Ipv4(192, 0, 2, 0), 24),      // documentation (TEST-NET-1)
        (Ipv4(192, 88, 99, 0), 24),    // deprecated 6to4 relay anycast
        (Ipv4(192, 168, 0, 0), 16),    // private
        (Ipv4(198, 18, 0, 0), 15),     // benchmarking
        (Ipv4(198, 51, 100, 0), 24),   // documentation (TEST-NET-2)
        (Ipv4(203, 0, 113, 0), 24),    // documentation (TEST-NET-3)
        (Ipv4(224, 0, 0, 0), 4),       // multicast
        (Ipv4(240, 0, 0, 0), 4),       // reserved, limited broadcast
    ];

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIPv4(ToUInt32(address.GetAddressBytes(), 0)),
            AddressFamily.InterNetworkV6 => IsPublicIPv6(address.GetAddressBytes()),
            _ => false,
        };
    }

    private static bool IsPublicIPv4(uint address)
    {
        foreach (var (network, prefixLength) in NonPublicIPv4)
        {
            var mask = uint.MaxValue << (32 - prefixLength);

            if ((address & mask) == network)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPublicIPv6(byte[] bytes)
    {
        // ::/96 holds the unspecified and loopback addresses and the deprecated IPv4-compatible ones (::a.b.c.d).
        if (StartsWith(bytes, [], 96))
        {
            var embedded = ToUInt32(bytes, 12);
            return embedded > 1 && IsPublicIPv4(embedded);
        }

        // IPv4-mapped (::ffff:a.b.c.d) and IPv4-translated (::ffff:0:a.b.c.d).
        if (StartsWith(bytes, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0xff], 96)
            || StartsWith(bytes, [0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0xff, 0, 0], 96))
        {
            return IsPublicIPv4(ToUInt32(bytes, 12));
        }

        // NAT64 with the well-known prefix 64:ff9b::/96.
        if (StartsWith(bytes, [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0], 96))
        {
            return IsPublicIPv4(ToUInt32(bytes, 12));
        }

        // Local-use NAT64 64:ff9b:1::/48 (RFC 8215): the operator picks the RFC 6052 prefix length, which the address
        // doesn't show. Only the /96 form 64:ff9b:1::a.b.c.d is unambiguous (any shorter length reads 0.0.0.0/8 from it);
        // the rest of the range is refused.
        if (StartsWith(bytes, [0x00, 0x64, 0xff, 0x9b, 0x00, 0x01], 48))
        {
            return StartsWith(bytes, [0x00, 0x64, 0xff, 0x9b, 0x00, 0x01, 0, 0, 0, 0, 0, 0], 96)
                && IsPublicIPv4(ToUInt32(bytes, 12));
        }

        // 6to4 2002:a.b.c.d::/48.
        if (StartsWith(bytes, [0x20, 0x02], 16))
        {
            return IsPublicIPv4(ToUInt32(bytes, 2));
        }

        // Teredo 2001::/32: the server address follows the prefix, the client's is the last 32 bits inverted.
        if (StartsWith(bytes, [0x20, 0x01, 0x00, 0x00], 32))
        {
            return IsPublicIPv4(ToUInt32(bytes, 4)) && IsPublicIPv4(~ToUInt32(bytes, 12));
        }

        // Only global unicast 2000::/3 is reachable; that excludes ::/8 (besides the forms above), 100::/64 discard,
        // 5f00::/16 SRv6, fc00::/7 unique local, fe80::/10 link local, fec0::/10 site local and ff00::/8 multicast.
        if ((bytes[0] & 0xe0) != 0x20)
        {
            return false;
        }

        // The rest of 2001::/23 (IETF protocol assignments, benchmarking, ORCHID), documentation 2001:db8::/32 and 3fff::/20.
        return !StartsWith(bytes, [0x20, 0x01, 0x00], 23)
            && !StartsWith(bytes, [0x20, 0x01, 0x0d, 0xb8], 32)
            && !StartsWith(bytes, [0x3f, 0xff, 0x00], 20);
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix, int prefixLength)
    {
        for (var bit = 0; bit < prefixLength; bit++)
        {
            var index = bit / 8;
            var mask = 0x80 >> (bit % 8);
            var expected = index < prefix.Length ? prefix[index] & mask : 0;

            if ((bytes[index] & mask) != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static uint ToUInt32(byte[] bytes, int offset)
    {
        return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static uint Ipv4(byte a, byte b, byte c, byte d)
    {
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d;
    }
}
