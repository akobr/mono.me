using System.Collections.Concurrent;
using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests;

// Hosting over one stub handler, fake clock and recording logger, without retries or a circuit that opens. Requests
// that run at once (ancestors) are answered by path, from routes that answer until they are replaced. Disposes the
// containers it built.
internal sealed class HostingFixture : IDisposable
{
    public const string Endpoint = "https://storyteller.example.com/api";
    public const string Scope = "@acme.billing.default";
    public const string ExecutionKey = "exe.northwind.invoicing.prod";

    // Long enough for a loaded machine, short enough for a test that would otherwise hang.
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _disposables = [];
    private readonly Lock _lock = new();

    public HostingFixture()
    {
        Http.Otherwise = (request, _) => Task.FromResult(Answer(request));
    }

    public StubHttpHandler Http { get; } = new();

    // Counts its timers, so a test advances it only after a poll armed the next one.
    public CountingTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));

    public RecordingLoggerFactory Logs { get; } = new();

    public Dictionary<string, string?> Environment { get; } = new()
    {
        ["STORYTELLER_ENDPOINT"] = Endpoint,
        ["STORYTELLER_KEY"] = $"{Scope}.{ExecutionKey}",
    };

    // The requests of the annotation reads, without those of configurations.
    public IReadOnlyList<RecordedRequest> AnnotationRequests => Http.Requests.Where(request => request.Uri.AbsolutePath.Contains("/annotations/", StringComparison.Ordinal)).ToList();

    public static string AnnotationPath(string annotationKey)
    {
        return $"/api/v1/acme/billing/default/annotations/{annotationKey}";
    }

    public static string ConfigurationPath(string annotationKey)
    {
        return $"/api/v1/acme/billing/default/configuration/{annotationKey}/resolved";
    }

    public Action<StorytellerConfigurationOptions> Configure(Action<StorytellerConfigurationOptions>? configure = null, bool withLogs = true)
    {
        return options =>
        {
            options.GetEnvironmentVariable = name => Environment.TryGetValue(name, out var value) ? value : null;
            options.PrimaryHandler = Http;
            options.TimeProvider = Time;
            options.NextRandom = () => 0.5;
            options.Resilience = new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero, MaxRetryAttempts = 0, MinimumThroughput = 1000 };

            if (withLogs)
            {
                options.LoggerFactory = Logs;
            }

            configure?.Invoke(options);
        };
    }

    public ServiceProvider BuildServices(Action<StorytellerConfigurationOptions>? configure = null, Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();
        services?.Invoke(collection);
        collection.AddStoryteller(Configure(configure));
        return Track(collection.BuildServiceProvider());
    }

    public void Route(string path, Func<HttpResponseMessage> respond)
    {
        _routes[path] = respond;
    }

    // The annotation of the key, from TestAnnotations.Json.
    public void RouteAnnotation(string annotationKey, Action<Newtonsoft.Json.Linq.JObject>? configure = null)
    {
        var json = TestAnnotations.Json(annotationKey, configure);
        Route(AnnotationPath(annotationKey), () => StubHttpHandler.Response(HttpStatusCode.OK, json));
    }

    public void RouteStatus(string annotationKey, HttpStatusCode status, string? json = null)
    {
        Route(AnnotationPath(annotationKey), () => StubHttpHandler.Response(status, json));
    }

    // The key of an execution and its four ancestors, all active.
    public void RouteExecution(string annotationKey = ExecutionKey)
    {
        var key = AnnotationKey.Parse(annotationKey);
        RouteAnnotation(annotationKey);

        foreach (var ancestor in key.GetAllAncestorKeys())
        {
            RouteAnnotation(ancestor.ToString());
        }
    }

    public void RouteConfiguration(string annotationKey, string name = "northwind")
    {
        var json = $$"""
            {
              "AnnotationKey": "{{annotationKey}}",
              "Version": 3,
              "Content": { "App": { "Name": "{{name}}" } },
              "Modified": { "At": "2026-10-19T08:30:00Z", "By": { "Kind": "Account", "Id": "user_1" } },
              "Hash": "a1b2c3d4"
            }
            """;
        Route(ConfigurationPath(annotationKey), () => StubHttpHandler.Response(HttpStatusCode.OK, json, ("ETag", "W/\"r.1\"")));
    }

    // Disposed at the end of the test, last tracked first.
    public T Track<T>(T item)
    {
        if (item is IDisposable disposable)
        {
            lock (_lock)
            {
                _disposables.Add(disposable);
            }
        }

        return item;
    }

    public void Dispose()
    {
        IDisposable[] disposables;

        lock (_lock)
        {
            disposables = [.. _disposables];
        }

        foreach (var disposable in disposables.Reverse())
        {
            disposable.Dispose();
        }
    }

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        return _routes.TryGetValue(request.RequestUri!.AbsolutePath, out var respond)
            ? respond()
            : throw new InvalidOperationException($"No route for {request.Method} {request.RequestUri.AbsolutePath}.");
    }
}
