using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration.UnitTests;

// The one route the provider reads, on a real port: the library's seam for a handler is internal to its own tests.
internal sealed class FakeApi : IAsyncDisposable
{
    public const string Key = "@acme.main.default.rst.playground";

    private readonly WebApplication _application;
    private volatile string _document;

    private FakeApi(WebApplication application, string document)
    {
        _application = application;
        _document = document;
    }

    public string Endpoint => $"{_application.Urls.Single()}/api";

    public static async Task<FakeApi> StartAsync(string content)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var application = builder.Build();
        var api = new FakeApi(application, Document(content));

        application.Run(context => context.Request.Path == "/api/v1/acme/main/default/configuration/rst.playground/resolved"
            ? context.Response.WriteAsync(api._document, context.RequestAborted)
            : Results.NotFound().ExecuteAsync(context));

        await application.StartAsync(TestContext.Current.CancellationToken);
        return api;
    }

    public void Publish(string content)
    {
        _document = Document(content);
    }

    public async ValueTask DisposeAsync()
    {
        await _application.DisposeAsync();
    }

    private static string Document(string content)
    {
        return new JObject
        {
            ["AnnotationKey"] = "rst.playground",
            ["Version"] = 1,
            ["Content"] = JObject.Parse(content),
        }.ToString();
    }
}
