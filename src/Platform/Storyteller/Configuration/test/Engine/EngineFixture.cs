using System.IO.Abstractions.TestingHelpers;
using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;

// Storyteller sources over one stub handler, fake clock, mock file system and recording logger, without retries or a
// circuit that opens. Disposes the configurations it built.
internal sealed class EngineFixture : IDisposable
{
    public const string PrivateNoCache = "private, no-cache";
    public static readonly string CacheDirectory = MockUnixSupport.Path(@"C:\cache\storyteller");

    // Long enough for a loaded machine, short enough for a test that would otherwise hang.
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly List<IDisposable> _disposables = [];
    private readonly Lock _lock = new();

    public EngineFixture(FakeTimeProvider? time = null)
    {
        Time = time ?? new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));
    }

    public StubHttpHandler Http { get; } = new();

    public FakeTimeProvider Time { get; }

    public MockFileSystem FileSystem { get; } = new();

    public RecordingLoggerFactory Logs { get; } = new();

    public Dictionary<string, string?> Environment { get; } = new()
    {
        ["STORYTELLER_ENDPOINT"] = TestDocuments.Endpoint,
        ["STORYTELLER_KEY"] = TestDocuments.Key,
    };

    // The jitter's r: 0.5 makes every delay the refresh interval itself.
    public double Random { get; set; } = 0.5;

    public static string Content(string name, int port = 80)
    {
        return $$"""{ "App": { "Name": "{{name}}", "Port": {{port}} } }""";
    }

    public static string Tag(int number)
    {
        return $"W/\"r.{number:D22}\"";
    }

    public static HttpResponseMessage Document(string name, string? etag, long version = 3, string? cacheControl = PrivateNoCache, int port = 80)
    {
        return StubHttpHandler.Response(HttpStatusCode.OK, TestDocuments.Json(Content(name, port), version), Headers(etag, cacheControl));
    }

    public static HttpResponseMessage NotModified(string? etag, string? cacheControl = PrivateNoCache)
    {
        return StubHttpHandler.Response(HttpStatusCode.NotModified, null, Headers(etag, cacheControl));
    }

    public Action<StorytellerConfigurationOptions> Configure(Action<StorytellerConfigurationOptions>? configure = null, bool withLogs = true)
    {
        return options =>
        {
            options.GetEnvironmentVariable = name => Environment.TryGetValue(name, out var value) ? value : null;
            options.PrimaryHandler = Http;
            options.FileSystem = FileSystem;
            options.TimeProvider = Time;
            options.NextRandom = () => Random;
            options.Resilience = new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero, MaxRetryAttempts = 0, MinimumThroughput = 1000 };

            if (withLogs)
            {
                options.LoggerFactory = Logs;
            }

            configure?.Invoke(options);
        };
    }

    public StorytellerConfigurationEngine Add(IConfigurationBuilder builder, Action<StorytellerConfigurationOptions>? configure = null)
    {
        builder.AddStoryteller(Configure(configure), out var refresher);
        return (StorytellerConfigurationEngine)refresher;
    }

    public (IConfigurationRoot Root, StorytellerConfigurationEngine Engine) Build(Action<StorytellerConfigurationOptions>? configure = null)
    {
        var builder = new ConfigurationBuilder();
        var engine = Add(builder, configure);
        return (Track(builder.Build()), engine);
    }

    public void RespondDocument(string name, string? etag, long version = 3, string? cacheControl = PrivateNoCache, int port = 80)
    {
        Http.Respond((_, _) => Task.FromResult(Document(name, etag, version, cacheControl, port)));
    }

    public void RespondNotModified(string? etag, string? cacheControl = PrivateNoCache)
    {
        Http.Respond((_, _) => Task.FromResult(NotModified(etag, cacheControl)));
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

    // Advances the clock by an interval and waits for the run the timer started, if any.
    public async Task AdvanceAsync(StorytellerConfigurationEngine engine, TimeSpan interval)
    {
        Time.Advance(interval);
        await engine.TimerRun.WaitAsync(Patience);
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

    private static (string Name, string Value)[] Headers(string? etag, string? cacheControl)
    {
        var headers = new List<(string Name, string Value)>();

        if (etag is not null)
        {
            headers.Add(("ETag", etag));
        }

        if (cacheControl is not null)
        {
            headers.Add(("Cache-Control", cacheControl));
        }

        return [.. headers];
    }
}
