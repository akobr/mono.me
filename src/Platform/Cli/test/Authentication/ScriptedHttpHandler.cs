using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.UnitTests.Authentication;

internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body, (string Name, string Value)[] Headers)> _responses = new();
    private readonly Queue<Exception> _failures = new();

    // Each request with its headers; the media type of its body is among them as Content-Type.
    public List<RecordedRequest> Requests { get; } = [];

    // The next response, with headers as the server sends them (for example ETag).
    public ScriptedHttpHandler Respond(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
    {
        _responses.Enqueue((status, body, headers));
        return this;
    }

    // The next request fails before a response, like an unreachable host.
    public ScriptedHttpHandler Fail(Exception exception)
    {
        _failures.Enqueue(exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sent = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var form = sent is null ? new Dictionary<string, string>() : ParseForm(sent);
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);

        if (request.Content?.Headers.ContentType is { } contentType)
        {
            headers["Content-Type"] = contentType.ToString();
        }

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), form, headers, sent));

        if (_failures.Count > 0)
        {
            throw _failures.Dequeue();
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}.");
        }

        var (status, body, responseHeaders) = _responses.Dequeue();
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        foreach (var (name, value) in responseHeaders)
        {
            // a content header (Content-Language, Last-Modified, ...) goes on the content
            if (!response.Headers.TryAddWithoutValidation(name, value))
            {
                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return response;
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        return body
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty);
    }
}
