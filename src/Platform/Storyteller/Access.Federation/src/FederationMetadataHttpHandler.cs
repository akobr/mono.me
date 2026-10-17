using System.Net;
using System.Net.Sockets;
using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller;

// The primary handler of the "federation-metadata" client. Issuer URLs come from project administrators, so the
// handler never uses a proxy or follows a redirect, and connects only to public addresses: it resolves the host
// itself and connects to the address it checked, so DNS rebinding cannot reach an internal one.
public static class FederationMetadataHttpHandler
{
    public const string HttpClientName = "federation-metadata";

    public const int MaxResponseBytes = 512 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static SocketsHttpHandler Create(FederationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = Timeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        // Development and tests may run their issuer on localhost.
        if (!options.AllowPrivateNetworks)
        {
            handler.ConnectCallback = ConnectToPublicAddressAsync;
        }

        return handler;
    }

    internal static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;
        IPAddress[] addresses = IPAddress.TryParse(endPoint.Host.Trim('[', ']'), out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken);
        var publicAddresses = addresses.Where(PublicAddressFilter.IsPublic).ToList();

        if (publicAddresses.Count < 1)
        {
            throw new HttpRequestException($"The host {endPoint.Host} has no public address.");
        }

        Exception? lastError = null;

        foreach (var address in publicAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                socket.Dispose();
                lastError = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException($"No public address of the host {endPoint.Host} accepted the connection.", lastError);
    }
}
