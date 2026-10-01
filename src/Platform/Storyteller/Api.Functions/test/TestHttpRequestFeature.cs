using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class TestHttpRequestFeature : IHttpRequestDataFeature
{
    private readonly HttpRequestData? _request;

    public TestHttpRequestFeature(HttpRequestData? request)
    {
        _request = request;
    }

    public ValueTask<HttpRequestData?> GetHttpRequestDataAsync(FunctionContext context)
    {
        return new ValueTask<HttpRequestData?>(_request);
    }
}
