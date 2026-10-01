using System.Collections.Specialized;
using System.Security.Claims;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class TestHttpRequestData : HttpRequestData
{
    public TestHttpRequestData(FunctionContext functionContext, IDictionary<string, string>? headers = null)
        : base(functionContext)
    {
        Url = new Uri("https://localhost/api/test");
        Method = "GET";
        Headers = headers is null
            ? new HttpHeadersCollection()
            : new HttpHeadersCollection(headers);
        Body = new MemoryStream();
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
