using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// Answers requests by URL and counts them; an unknown URL is a 404.
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _responses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    // Set to hold every request until the test releases it.
    public TaskCompletionSource? Gate { get; set; }

    public int TotalRequests => _counts.Values.Sum();

    public void SetJson(string url, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses[url] = () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    public void Set(string url, Func<HttpResponseMessage> response)
    {
        _responses[url] = response;
    }

    public int Count(string url) => _counts.GetValueOrDefault(url);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        _counts.AddOrUpdate(url, 1, (_, count) => count + 1);

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return _responses.TryGetValue(url, out var response)
            ? response()
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
