using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// The Storyteller API as the SDK clients see it: every request is recorded, Respond decides the answer (200 {} when
// unset).
internal sealed class RecordingApiHandler : HttpMessageHandler
{
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        // the raw values: a tag such as * or "a1.x" must not be reformatted
        var headers = request.Headers.NonValidated.ToDictionary(
            header => header.Key,
            header => string.Join(", ", header.Value),
            StringComparer.OrdinalIgnoreCase);

        Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body)
        {
            ContentType = request.Content?.Headers.ContentType?.MediaType,
            Headers = headers,
        });
        return Respond?.Invoke(request) ?? Json("{}");
    }
}
