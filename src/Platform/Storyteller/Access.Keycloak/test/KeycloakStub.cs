using System.Net;
using System.Text;

namespace _42.Platform.Storyteller.Access.Keycloak.UnitTests;

// A Keycloak admin API by method and path; records every request with its body and Authorization.
internal sealed class KeycloakStub : HttpMessageHandler, IHttpClientFactory
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public List<(HttpMethod Method, string Path, string? Authorization, string? Body)> Requests { get; } = [];

    public KeycloakStub On(HttpMethod method, string path, HttpStatusCode status, string body = "", Uri? location = null)
    {
        _routes[$"{method} {path}"] = () =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.Location = location;
            return response;
        };
        return this;
    }

    public KeycloakStub OnThrow(HttpMethod method, string path, Exception exception)
    {
        _routes[$"{method} {path}"] = () => throw exception;
        return this;
    }

    public KeycloakStub WithAdminToken()
    {
        return On(HttpMethod.Post, "/realms/master/protocol/openid-connect/token", HttpStatusCode.OK, """{"access_token":"admin-token"}""");
    }

    public HttpClient CreateClient(string name)
    {
        return new HttpClient(this, disposeHandler: false);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));

        return _routes.TryGetValue($"{request.Method} {request.RequestUri.AbsolutePath}", out var respond)
            ? respond()
            : new HttpResponseMessage(HttpStatusCode.NotImplemented);
    }
}
