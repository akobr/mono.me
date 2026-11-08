using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// A minimal HTTP/1.1 server on a loopback port for the self tests that need sform to get a little further than a closed
// port: each request (Content-Length bodies, one request per connection) goes to the handler, whose answer is written with
// Connection: close. Every request is kept in Requests, so a test checks what sform sent.
internal sealed class StubApi : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly Func<StubRequest, StubResponse> handler;
    private readonly CancellationTokenSource stop = new();
    private readonly Task serving;

    public StubApi(Func<StubRequest, StubResponse> handler)
    {
        this.handler = handler;
        listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/api";
        serving = Task.Run(ServeAsync);
    }

    // The API's base URL with /api, as Target:BaseUrl or STORYTELLER_ENDPOINT take it.
    public string Url { get; }

    public ConcurrentQueue<StubRequest> Requests { get; } = new();

    // "GET /api/v1/… → 200" per request, for a failure's message.
    public string Describe()
    {
        return Requests.IsEmpty ? "(no request)" : string.Join('\n', Requests.Select(request => $"{request.Method} {request.Path} → {request.Status}"));
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        listener.Stop();
        try
        {
            await serving;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // stopped
        }

        stop.Dispose();
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellation)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one, cancellation) == 1)
        {
            if (one[0] == '\n')
            {
                return Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
            }

            bytes.Add(one[0]);
        }

        return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);
    }

    private async Task ServeAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stop.Token);
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var requestLine = await ReadLineAsync(stream, stop.Token);
            if (string.IsNullOrEmpty(requestLine))
            {
                return;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await ReadLineAsync(stream, stop.Token) is { Length: > 0 } header)
            {
                var colon = header.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    headers[header[..colon].Trim()] = header[(colon + 1)..].Trim();
                }
            }

            var body = string.Empty;
            if (headers.TryGetValue("Content-Length", out var length) && int.TryParse(length, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size > 0)
            {
                var buffer = new byte[size];
                await stream.ReadExactlyAsync(buffer, stop.Token);
                body = Encoding.UTF8.GetString(buffer);
            }

            var parts = requestLine.Split(' ');
            var request = new StubRequest(parts[0], parts.Length > 1 ? parts[1] : "/", headers, body);
            var response = handler(request);
            Requests.Enqueue(request with { Status = response.Status });

            var payload = Encoding.UTF8.GetBytes(response.Body);
            var head = $"HTTP/1.1 {response.Status} {(response.Status < 400 ? "OK" : "Error")}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), stop.Token);
            await stream.WriteAsync(payload, stop.Token);
            await stream.FlushAsync(stop.Token);
        }
    }
}
