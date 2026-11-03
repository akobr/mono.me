using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// The handler of the local emulators' clients: "localhost" connects over IPv4 first, then IPv6; other hosts as usual.
public class EmulatorHttpHandlerTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("localhost.")]
    public void Candidates_OfLocalhost_AreIpv4LoopbackFirst(string host)
    {
        var candidates = EmulatorHttpHandler.Candidates(new DnsEndPoint(host, 8081));

        candidates[0].ShouldBe(new IPEndPoint(IPAddress.Loopback, 8081));
        if (Socket.OSSupportsIPv6)
        {
            candidates.Count.ShouldBe(2);
            candidates[1].ShouldBe(new IPEndPoint(IPAddress.IPv6Loopback, 8081));
        }
        else
        {
            candidates.Count.ShouldBe(1);
        }
    }

    [Theory]
    [InlineData("cosmos.example.com")]
    [InlineData("127.0.0.1")]
    [InlineData("localhost.example.com")]
    public void Candidates_OfAnotherHost_AreTheHostItself(string host)
    {
        var endPoint = new DnsEndPoint(host, 443);

        EmulatorHttpHandler.Candidates(endPoint).ShouldBe([endPoint]);
    }

    [Fact]
    public async Task ConnectAsync_Localhost_ReachesAnIpv4OnlyListener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptSocketAsync(TestContext.Current.CancellationToken);
        var watch = Stopwatch.StartNew();

        await using var stream = await EmulatorHttpHandler.ConnectAsync(new DnsEndPoint("localhost", port), TestContext.Current.CancellationToken);

        watch.Stop();
        using var server = await accepted;
        ((IPEndPoint)server.RemoteEndPoint!).Address.ShouldBe(IPAddress.Loopback);

        // On Windows the IPv6 attempt of the default connect waits about 2 s before IPv4 is tried.
        if (OperatingSystem.IsWindows())
        {
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task ConnectAsync_Localhost_FallsBackToAnIpv6OnlyListener()
    {
        Assert.SkipUnless(Socket.OSSupportsIPv6, "The OS has no IPv6.");

        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener.Start();
        }
        catch (SocketException)
        {
            Assert.Skip("The IPv6 loopback is not available.");
            return;
        }

        using (listener)
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.SkipWhen(CanConnect(new IPEndPoint(IPAddress.Loopback, port)), $"Something listens on 127.0.0.1:{port}.");
            var accepted = listener.AcceptSocketAsync(TestContext.Current.CancellationToken);

            await using var stream = await EmulatorHttpHandler.ConnectAsync(new DnsEndPoint("localhost", port), TestContext.Current.CancellationToken);

            using var server = await accepted;
            ((IPEndPoint)server.RemoteEndPoint!).Address.ShouldBe(IPAddress.IPv6Loopback);
        }
    }

    [Fact]
    public async Task ConnectAsync_NothingListening_ThrowsTheSocketError()
    {
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        Assert.SkipWhen(Socket.OSSupportsIPv6 && CanConnect(new IPEndPoint(IPAddress.IPv6Loopback, port)), $"Something listens on [::1]:{port}.");

        await Should.ThrowAsync<SocketException>(
            async () => await EmulatorHttpHandler.ConnectAsync(new DnsEndPoint("localhost", port), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_SendsTheRequestByTheNameLocalhost_ToAnIpv4OnlyServer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var served = ServeOneAsync(listener, TestContext.Current.CancellationToken);

        using var client = new HttpClient(EmulatorHttpHandler.Create());
        using var response = await client.GetAsync(new Uri($"http://localhost:{port}/probe"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("ok");
        (await served).ShouldContain($"Host: localhost:{port}");
    }

    // Answers one HTTP/1.1 request with 200 "ok" and returns the request's head.
    private static async Task<string> ServeOneAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptSocketAsync(cancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var head = new StringBuilder();
        var buffer = new byte[1024];
        while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            head.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), cancellationToken);
        return head.ToString();
    }

    private static bool CanConnect(IPEndPoint endPoint)
    {
        using var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Connect(endPoint);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
