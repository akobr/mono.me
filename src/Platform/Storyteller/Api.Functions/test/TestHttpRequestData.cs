using System.Collections.Specialized;
using System.Security.Claims;
using System.Text;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class TestHttpRequestData : HttpRequestData
{
    public TestHttpRequestData(
        FunctionContext functionContext,
        IDictionary<string, string>? headers = null,
        string method = "GET",
        string? body = null,
        Uri? url = null)
        : base(functionContext)
    {
        Url = url ?? new Uri("https://localhost/api/test");
        Method = method;
        Headers = headers is null
            ? new HttpHeadersCollection()
            : new HttpHeadersCollection(headers);
        Body = body is null ? new MemoryStream() : new MemoryStream(Encoding.UTF8.GetBytes(body));
        Cookies = [];
        IdentityList = [];
        Query = new NameValueCollection();
    }

    public List<ClaimsIdentity> IdentityList { get; }

    public override Stream Body { get; }

    public override HttpHeadersCollection Headers { get; }

    public override IReadOnlyCollection<IHttpCookie> Cookies { get; }

    public override Uri Url { get; }

    public override IEnumerable<ClaimsIdentity> Identities => IdentityList;

    public override string Method { get; }

    public override NameValueCollection Query { get; }

    public override HttpResponseData CreateResponse()
    {
        return new TestHttpResponseData(FunctionContext);
    }
}
