using System.Net;
using System.Text;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

// Answers Microsoft Graph requests by method and path; records every request with its body.
internal sealed class GraphStubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes = new(StringComparer.Ordinal);

    public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = [];

    public GraphStubHandler On(HttpMethod method, string path, HttpStatusCode status, string body = "")
    {
        _routes[$"{method} {path}"] = (status, body);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri!;
        Requests.Add((request.Method, Uri.UnescapeDataString(uri.PathAndQuery), body));

        if (!_routes.TryGetValue($"{request.Method} {uri.AbsolutePath}", out var route))
        {
            return new HttpResponseMessage(HttpStatusCode.NotImplemented)
            {
                Content = new StringContent($"No stub for {request.Method} {uri.AbsolutePath}"),
            };
        }

        return new HttpResponseMessage(route.Status)
        {
            Content = new StringContent(route.Body, Encoding.UTF8, "application/json"),
        };
    }
}
