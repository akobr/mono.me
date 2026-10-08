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
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public ScriptedHttpHandler Respond(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = request.Content is null
            ? new Dictionary<string, string>()
            : ParseForm(await request.Content.ReadAsStringAsync(cancellationToken));
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), form));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}.");
        }

        var (status, body) = _responses.Dequeue();
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
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
