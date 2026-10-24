using System.Net;
using System.Text;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// Answers from a queue, then from Otherwise, and records every request with its raw headers.
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Otherwise { get; set; }

    public bool IsDisposed { get; private set; }

    public static HttpResponseMessage Response(HttpStatusCode status, string? json = null, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status);

        if (json is not null)
        {
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    public StubHttpHandler Respond(HttpStatusCode status, string? json = null, params (string Name, string Value)[] headers)
    {
        return Respond((_, _) => Task.FromResult(Response(status, json, headers)));
    }

    public StubHttpHandler Respond(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        lock (_lock)
        {
            _responses.Enqueue(respond);
        }

        return this;
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.NonValidated.ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond;

        lock (_lock)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), headers));
            respond = _responses.Count > 0 ? _responses.Dequeue() : Otherwise;
        }

        if (respond is null)
        {
            throw new InvalidOperationException($"No response for {request.Method} {request.RequestUri}.");
        }

        var response = await respond(request, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }
}
