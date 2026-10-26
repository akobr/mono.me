using System.Collections.Concurrent;
using System.Net;
using System.Text;

using Microsoft.AspNetCore.WebUtilities;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// The API's token endpoint as the exchange client sees it: every request is recorded with its form, Respond decides the
// answer (a token for the subject when unset). An answer that never comes ends with the request's cancellation.
internal sealed class TokenEndpointHandler : HttpMessageHandler
{
    public ConcurrentQueue<TokenRequest> Requests { get; } = new();

    public Func<TokenRequest, Task<HttpResponseMessage>>? Respond { get; set; }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    public static HttpResponseMessage Token(string accessToken, int expiresIn = 600, string restrictions = "[\"NoSecretReveal\"]")
    {
        return Json($$"""{"access_token":"{{accessToken}}","issued_token_type":"urn:ietf:params:oauth:token-type:access_token","token_type":"Bearer","expires_in":{{expiresIn}},"scope":"User.Impersonation","restrictions":{{restrictions}}}""");
    }

    public static HttpResponseMessage Error(string error, string? description = null, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        return Json(description is null ? $$"""{"error":"{{error}}"}""" : $$"""{"error":"{{error}}","error_description":"{{description}}"}""", status);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new TokenRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            QueryHelpers.ParseQuery(body));
        Requests.Enqueue(recorded);

        return Respond is null
            ? Token($"storyteller:{recorded.Form["subject_token"]}")
            : await Respond(recorded).WaitAsync(cancellationToken);
    }
}
