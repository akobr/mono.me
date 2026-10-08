using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.OpenApi;

/// <summary>
/// Runs one OpenAPI render at a time.
/// </summary>
/// <remarks>
/// The extension registers a single <see cref="IOpenApiHttpTriggerContext"/> for the process.
/// Its schema dictionary is filled while a document is built. A second overlapping request
/// inserts the same schema name again, and the first name it hits is <c>AccountCreate</c>.
/// </remarks>
internal sealed class SerialOpenApiTriggerFunction : IOpenApiTriggerFunction
{
    private readonly IOpenApiTriggerFunction _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SerialOpenApiTriggerFunction(IOpenApiTriggerFunction inner)
    {
        _inner = inner;
    }

    public Task<HttpResponseData> RenderSwaggerDocument(HttpRequestData req, string extension, FunctionContext ctx)
    {
        return Run(ctx, () => _inner.RenderSwaggerDocument(req, extension, ctx));
    }

    public Task<HttpResponseData> RenderOpenApiDocument(HttpRequestData req, string version, string extension, FunctionContext ctx)
    {
        return Run(ctx, () => _inner.RenderOpenApiDocument(req, version, extension, ctx));
    }

    public Task<HttpResponseData> RenderSwaggerUI(HttpRequestData req, FunctionContext ctx)
    {
        return Run(ctx, () => _inner.RenderSwaggerUI(req, ctx));
    }

    public Task<HttpResponseData> RenderOAuth2Redirect(HttpRequestData req, FunctionContext ctx)
    {
        return Run(ctx, () => _inner.RenderOAuth2Redirect(req, ctx));
    }

    private async Task<HttpResponseData> Run(FunctionContext context, Func<Task<HttpResponseData>> render)
    {
        await _gate.WaitAsync(context.CancellationToken);
        try
        {
            return await render();
        }
        finally
        {
            _gate.Release();
        }
    }
}
