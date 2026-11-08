using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleExecutor.UnitTests;

// The routes of who am I, the catalogs and the documents, on a real port, with the playground's own data: two subjects
// of rst.playground that share the unit nightly-export, and the unit archive that no execution names. Annotations carry
// the AnnotationType discriminator, which makes the SDK read their relation names; list items carry no relation names,
// as the API lists them. A path it does not hold answers 404. Records the paths it was asked for.
internal sealed class FakeApi : IAsyncDisposable
{
    public const string Scope = "@acme.main.default";

    private const string RootPath = "/api/v1/acme/main/default";

    private readonly WebApplication _application;
    private readonly ConcurrentDictionary<string, string> _routes = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _requests = new();

    private FakeApi(WebApplication application)
    {
        _application = application;

        AddAnnotation("rst.playground", new JObject { ["UnitNames"] = new JArray("nightly-export", "archive") });
        AddAnnotation("unt.playground.nightly-export", new JObject { ["UnitType"] = "job" });
        AddAnnotation("unt.playground.archive");

        foreach (var (subject, plan) in new[] { ("contoso", "silver"), ("northwind", "gold") })
        {
            AddAnnotation($"sbt.{subject}");
            AddAnnotation($"cnt.{subject}.production");
            AddAnnotation($"usg.{subject}.playground", new JObject { ["Values"] = new JObject { ["plan"] = plan } });
            AddAnnotation($"exe.{subject}.playground.production", new JObject { ["UnitNames"] = new JArray("nightly-export") });
            AddConfiguration($"exe.{subject}.playground.production", $$"""{ "subject": "{{subject}}" }""");
            AddConfiguration($"uxe.{subject}.playground.production.nightly-export", $$"""{ "target": "{{subject}}-archive" }""");
        }

        AddConfiguration("unt.playground.nightly-export", """{ "schedule": "0 2 * * *" }""");
        AddConfiguration("unt.playground.archive", """{ "schedule": "0 3 * * 0" }""");
        AddList("executions", "exe.contoso.playground.production", "exe.northwind.playground.production");
        AddList("usages", "usg.contoso.playground", "usg.northwind.playground");
        AddList("units", "unt.playground.nightly-export", "unt.playground.archive");
    }

    public string Endpoint => $"{_application.Urls.Single()}/api";

    // The paths asked for, without the query.
    public IReadOnlyList<string> Requests => [.. _requests];

    public static async Task<FakeApi> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var application = builder.Build();
        var api = new FakeApi(application);

        application.Run(context =>
        {
            api._requests.Enqueue(context.Request.Path.Value ?? string.Empty);

            if (api._routes.TryGetValue(context.Request.Path.Value ?? string.Empty, out var body))
            {
                return context.Response.WriteAsync(body, context.RequestAborted);
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        await application.StartAsync(TestContext.Current.CancellationToken);
        return api;
    }

    // A list of the descendants of rst.playground.
    public static string ListPath(string list)
    {
        return $"{RootPath}/annotations/rst.playground/{list}";
    }

    // A name the env format cannot write, or a read that fails, for one document among the others.
    public void AddConfiguration(string annotationKey, string content)
    {
        _routes[$"{RootPath}/configuration/{annotationKey}/resolved"] = new JObject
        {
            ["AnnotationKey"] = annotationKey,
            ["Version"] = 1,
            ["Content"] = JObject.Parse(content),
        }.ToString();
    }

    public void AddAnnotation(string annotationKey, JObject? members = null)
    {
        _routes[$"{RootPath}/annotations/{annotationKey}"] = Annotation(annotationKey, members).ToString();
    }

    public void RemoveConfiguration(string annotationKey)
    {
        _routes.TryRemove($"{RootPath}/configuration/{annotationKey}/resolved", out _);
    }

    // The list answers 404, as for a platform that does not know it.
    public void RemoveList(string list)
    {
        _routes.TryRemove(ListPath(list), out _);
    }

    public async ValueTask DisposeAsync()
    {
        await _application.DisposeAsync();
    }

    private static JObject Annotation(string annotationKey, JObject? members = null)
    {
        var key = AnnotationKey.Parse(annotationKey);
        var annotation = new JObject
        {
            ["AnnotationType"] = key.Type.ToString(),
            ["ProjectName"] = "main",
            ["ViewName"] = "default",
            ["AnnotationKey"] = annotationKey,
            ["Name"] = key.Name,
        };

        annotation.Merge(members);
        return annotation;
    }

    private void AddList(string list, params string[] annotationKeys)
    {
        _routes[ListPath(list)] = new JObject
        {
            ["Annotations"] = new JArray(annotationKeys.Select(key => Annotation(key))),
            ["Count"] = annotationKeys.Length,
        }.ToString();
    }
}
