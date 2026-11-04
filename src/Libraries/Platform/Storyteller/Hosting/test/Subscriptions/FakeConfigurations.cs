using System.Collections.Concurrent;
using System.Net;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Subscriptions;

// The resolved configurations of the fixture's view as the API answers them: a document per annotation key with its
// tag, a 304 when the request sends that tag, and failures by key. Answers on the fixture's handler before what was
// there; counts the answers in flight, and holds them on request.
internal sealed class FakeConfigurations
{
    private const string ConfigurationPath = "/api/v1/acme/billing/default/configuration/";
    private const string ResolvedSuffix = "/resolved";

    private readonly HostingFixture _fixture;
    private readonly ConcurrentDictionary<string, Stored> _documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _failures = new(StringComparer.Ordinal);
    private TaskCompletionSource? _gate;
    private int _tags;
    private int _inFlight;
    private int _maxInFlight;
    private int _held;

    public FakeConfigurations(HostingFixture fixture)
    {
        _fixture = fixture;
        var answer = fixture.Http.Otherwise!;
        fixture.Http.Otherwise = (request, cancellationToken) => Answer(request, cancellationToken) ?? answer(request, cancellationToken);
    }

    // The most answers in flight at once.
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    // The answers that wait for Release().
    public int Held => Volatile.Read(ref _held);

    // Answers wait from now until Release(), so the reads overlap for as long as a test needs.
    public void Hold()
    {
        Volatile.Write(ref _gate, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    public void Release()
    {
        Volatile.Read(ref _gate)?.TrySetResult();
    }

    // The document of the key under a new tag. content: the JSON of its content; labels and values: of the
    // configuration, which the flattened data does not hold.
    public string Put(string annotationKey, string content, long version = 3, string[]? labels = null, JObject? values = null)
    {
        var tag = $"W/\"r.{Interlocked.Increment(ref _tags)}\"";
        var json = new JObject
        {
            ["AnnotationKey"] = annotationKey,
            ["Version"] = version,
            ["Content"] = JObject.Parse(content),
            ["Labels"] = new JArray(labels ?? []),
            ["Values"] = values ?? new JObject(),
            ["Modified"] = new JObject { ["At"] = "2026-10-19T08:30:00Z", ["By"] = new JObject { ["Kind"] = "Account", ["Id"] = "user_1" } },
            ["Hash"] = "a1b2c3d4",
        };

        _documents[annotationKey] = new Stored(json.ToString(), tag);
        return tag;
    }

    // { "App": { "Name": name, "Port": port } }
    public string PutApp(string annotationKey, string name, int port = 80, long version = 3)
    {
        return Put(annotationKey, $$"""{ "App": { "Name": "{{name}}", "Port": {{port}} } }""", version);
    }

    public string? Tag(string annotationKey)
    {
        return _documents.TryGetValue(annotationKey, out var stored) ? stored.Tag : null;
    }

    // Reads of the key fail with the status.
    public void Fail(string annotationKey, HttpStatusCode status, string? json = null)
    {
        _failures[annotationKey] = () => StubHttpHandler.Response(status, json);
    }

    public void Heal(string annotationKey)
    {
        _failures.TryRemove(annotationKey, out _);
    }

    public IReadOnlyList<RecordedRequest> Reads(string annotationKey)
    {
        return _fixture.Http.Requests.Where(request => request.Uri.AbsolutePath == $"{ConfigurationPath}{annotationKey}{ResolvedSuffix}").ToList();
    }

    public IReadOnlyList<RecordedRequest> AllReads()
    {
        return _fixture.Http.Requests.Where(request => request.Uri.AbsolutePath.StartsWith(ConfigurationPath, StringComparison.Ordinal)).ToList();
    }

    private Task<HttpResponseMessage>? Answer(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (!path.StartsWith(ConfigurationPath, StringComparison.Ordinal) || !path.EndsWith(ResolvedSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var annotationKey = Uri.UnescapeDataString(path[ConfigurationPath.Length..^ResolvedSuffix.Length]);
        var tag = request.Headers.NonValidated.TryGetValues("If-None-Match", out var values) ? string.Join(", ", values) : null;
        return RespondAsync(annotationKey, tag, cancellationToken);
    }

    private async Task<HttpResponseMessage> RespondAsync(string annotationKey, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        var inFlight = Interlocked.Increment(ref _inFlight);
        int max;

        while (inFlight > (max = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, inFlight, max) != max)
        {
        }

        try
        {
            if (Volatile.Read(ref _gate) is { } gate)
            {
                Interlocked.Increment(ref _held);

                try
                {
                    await gate.Task.WaitAsync(cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref _held);
                }
            }

            if (_failures.TryGetValue(annotationKey, out var failure))
            {
                return failure();
            }

            if (!_documents.TryGetValue(annotationKey, out var stored))
            {
                return StubHttpHandler.Response(HttpStatusCode.NotFound, """{ "Message": "No configuration.", "ErrorCode": "ConfigurationNotFound" }""");
            }

            return string.Equals(ifNoneMatch, stored.Tag, StringComparison.Ordinal)
                ? StubHttpHandler.Response(HttpStatusCode.NotModified, null, ("ETag", stored.Tag))
                : StubHttpHandler.Response(HttpStatusCode.OK, stored.Json, ("ETag", stored.Tag));
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private sealed record Stored(string Json, string Tag);
}
