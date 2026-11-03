using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace _42.Platform.Storyteller;

// The primary handler of the HTTP clients of local emulators (the Cosmos DB emulator, Lowkey Vault); for development
// only. The emulators are self-signed, so any server certificate is accepted. On Windows "localhost" resolves to ::1
// first, and a connection to a port that listens on 127.0.0.1 only (the Windows Cosmos DB emulator, Docker's port
// publishing) waits about 2 s for the refused IPv6 attempt, for every new pooled connection: the handler connects
// "localhost" to 127.0.0.1 first and to ::1 only when IPv4 fails. The URI, the SNI and the Host header stay
// "localhost" (Lowkey Vault's key ids carry the request host), so the connection strings are not rewritten.
// Internal: it accepts any certificate, so only the emulator clients of Backend.CosmosDb and KeyValues.Azure.KeyVault
// see it (InternalsVisibleTo). The 15 s connect timeout covers both attempts; a refused IPv4 attempt takes about 2 s.
internal static class EmulatorHttpHandler
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    public static SocketsHttpHandler Create()
    {
        return new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = ConnectTimeout,
            ConnectCallback = static (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, cancellationToken),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            },
        };
    }

    // The endpoints a connection tries, in order: 127.0.0.1, then ::1 (where the OS has IPv6) for "localhost" in any
    // case, also with the trailing dot of a fully qualified name; the endpoint itself for any other host.
    internal static IReadOnlyList<EndPoint> Candidates(DnsEndPoint endPoint)
    {
        ArgumentNullException.ThrowIfNull(endPoint);

        if (!IsLocalhost(endPoint.Host))
        {
            return [endPoint];
        }

        var ipv4 = new IPEndPoint(IPAddress.Loopback, endPoint.Port);
        return Socket.OSSupportsIPv6 ? [ipv4, new IPEndPoint(IPAddress.IPv6Loopback, endPoint.Port)] : [ipv4];
    }

    // The candidates in order, the first that connects wins. A DNS endpoint connects through a dual-mode socket to
    // every address of the name, as SocketsHttpHandler does by default. When every candidate fails, the first failure
    // (IPv4's, for localhost) is thrown.
    internal static async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, CancellationToken cancellationToken)
    {
        SocketException? firstError = null;

        foreach (var candidate in Candidates(endPoint))
        {
            var socket = candidate is IPEndPoint address
                ? new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true }
                : new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(candidate, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                firstError ??= exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw firstError ?? new SocketException((int)SocketError.HostNotFound);
    }

    private static bool IsLocalhost(string host)
    {
        return string.Equals(host.TrimEnd('.'), "localhost", StringComparison.OrdinalIgnoreCase);
    }
}
