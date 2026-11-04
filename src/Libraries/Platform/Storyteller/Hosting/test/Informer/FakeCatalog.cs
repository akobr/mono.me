using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;

// The annotations of the fixture's view as the API holds them, for the informer. The descendants routes of a
// responsibility list its active executions or usages by key in descending order (pages have no order, so the informer
// must sort what it emits), in pages of PageSize with base64 tokens, with lossy values ("list:" and the stored JSON);
// point reads honour includeInactive and includeDeleted. Put and Update stamp Modified, as the API's writes do. Answers
// on the fixture's handler before its routes; the server's clock is the fixture's. Counts the answers in flight, and
// holds the point reads of executions and usages on request.
internal sealed class FakeCatalog
{
    public const string Responsibility = "invoicing";
    public const string ResponsibilityKey = $"rst.{Responsibility}";
    public const string Context = "prod";

    private const string AnnotationsPath = "/api/v1/acme/billing/default/annotations/";

    private static readonly DateTimeOffset FirstWrite = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly HostingFixture _fixture;
    private readonly ConcurrentDictionary<string, JObject> _annotations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _unlisted = new(StringComparer.Ordinal);
    private TaskCompletionSource? _gate;
    private int _writes;
    private int _inFlight;
    private int _maxInFlight;
    private int _held;

    public FakeCatalog(HostingFixture fixture)
    {
        _fixture = fixture;
        var routes = fixture.Http.Otherwise!;
        fixture.Http.Otherwise = (request, cancellationToken) => Answer(request, cancellationToken) ?? routes(request, cancellationToken);
        Put(ResponsibilityKey);
    }

    public int PageSize { get; set; } = 1000;

    // A real delay per answer, so that reads overlap.
    public TimeSpan Latency { get; set; }

    // The most answers in flight at once.
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    // The point reads that wait for ReleaseReads().
    public int HeldReads => Volatile.Read(ref _held);

    public static string ExecutionKey(string subject, string context = Context)
    {
        return $"exe.{subject}.{Responsibility}.{context}";
    }

    public static string UsageKey(string subject)
    {
        return $"usg.{subject}.{Responsibility}";
    }

    public static string Full(string annotationKey)
    {
        return $"{HostingFixture.Scope}.{annotationKey}";
    }

    public static string ListPath(string descendants)
    {
        return HostingFixture.AnnotationPath($"{ResponsibilityKey}/{descendants}");
    }

    // The token of the page that starts at offset: base64 with '+', '/' or '=', as the API's tokens may have.
    public static string Token(string descendants, int offset)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{descendants}|{offset}>?"));
    }

    // stamp: a Modified stamp before configure runs; without one the informer tells changes by a hash.
    public void Put(string annotationKey, Action<JObject>? configure = null, bool stamp = true)
    {
        var json = JObject.Parse(TestAnnotations.Json(annotationKey, json =>
        {
            if (stamp)
            {
                Stamp(json);
            }

            configure?.Invoke(json);
        }));

        _annotations[annotationKey] = json;
    }

    // A usage and the execution of the subject in the context, both active.
    public void Subscribe(string subject, string context = Context, Action<JObject>? execution = null, Action<JObject>? usage = null, bool stamp = true)
    {
        Put(UsageKey(subject), usage, stamp);
        Put(ExecutionKey(subject, context), execution, stamp);
    }

    public void Update(string annotationKey, Action<JObject> change, bool stamp = true)
    {
        var json = (JObject)_annotations[annotationKey].DeepClone();
        change(json);

        if (stamp)
        {
            Stamp(json);
        }

        _annotations[annotationKey] = json;
    }

    // Point reads of executions and usages wait from now until ReleaseReads(), so they overlap for as long as a test
    // needs; the read of the responsibility and the lists, which run alone or in pairs, do not.
    public void HoldReads()
    {
        Volatile.Write(ref _gate, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    public void ReleaseReads()
    {
        Volatile.Read(ref _gate)?.TrySetResult();
    }

    // Gone, as after a purge: a point read answers 404 without a body.
    public void Purge(string annotationKey)
    {
        _annotations.TryRemove(annotationKey, out _);
    }

    // The lists miss it, while point reads still find it.
    public void Unlist(string annotationKey)
    {
        _unlisted[annotationKey] = true;
    }

    public void Relist(string annotationKey)
    {
        _unlisted.TryRemove(annotationKey, out _);
    }

    // Point reads of the key fail with the status.
    public void Fail(string annotationKey, HttpStatusCode status, string? json = null)
    {
        _failures[annotationKey] = () => StubHttpHandler.Response(status, json);
    }

    // Point reads of the key answer the stored annotation changed, while the lists answer what is stored: the list is
    // the platform's view of now, and this process's clock may differ.
    public void ReadAs(string annotationKey, Action<JObject> change)
    {
        var json = (JObject)_annotations[annotationKey].DeepClone();
        change(json);
        _failures[annotationKey] = () => StubHttpHandler.Response(HttpStatusCode.OK, json.ToString());
    }

    // The page of the list at token fails; null: the first page.
    public void FailPage(string descendants, string? token, HttpStatusCode status, string? json = null)
    {
        _failures[$"{ResponsibilityKey}/{descendants}#{token}"] = () => StubHttpHandler.Response(status, json);
    }

    public void Heal()
    {
        _failures.Clear();
    }

    public IReadOnlyList<RecordedRequest> Reads(string annotationKey)
    {
        return _fixture.Http.Requests.Where(request => request.Uri.AbsolutePath == HostingFixture.AnnotationPath(annotationKey)).ToList();
    }

    public IReadOnlyList<RecordedRequest> Lists(string descendants)
    {
        return _fixture.Http.Requests.Where(request => request.Uri.AbsolutePath == ListPath(descendants)).ToList();
    }

    private static IReadOnlyDictionary<string, string> Query(Uri uri)
    {
        return uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty, StringComparer.Ordinal);
    }

    // As the list reads values through another serializer: deterministic, but not what was stored.
    private static JObject ListItem(JObject stored)
    {
        var item = (JObject)stored.DeepClone();

        if (item["Values"] is JObject values)
        {
            foreach (var value in values.Properties().ToList())
            {
                value.Value = $"list:{value.Value.ToString(Formatting.None)}";
            }
        }

        return item;
    }

    private static int Offset(string descendants, string? token)
    {
        if (token is null)
        {
            return 0;
        }

        var text = Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var prefix = $"{descendants}|";
        return int.Parse(text[prefix.Length..^2], System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Stamp(JObject json)
    {
        json["Modified"] = new JObject
        {
            ["At"] = FirstWrite.AddSeconds(Interlocked.Increment(ref _writes)).ToString("O"),
            ["By"] = new JObject { ["Kind"] = "Account", ["Id"] = "user_1" },
        };
    }

    private bool IsActive(JObject json)
    {
        var now = _fixture.Time.GetUtcNow();

        return json["IsDisabled"]?.Value<bool>() != true
            && !(json["ValidFrom"] is { Type: not JTokenType.Null } validFrom && (DateTimeOffset)validFrom > now)
            && !(json["ExpiresAt"] is { Type: not JTokenType.Null } expiresAt && (DateTimeOffset)expiresAt <= now);
    }

    private Task<HttpResponseMessage>? Answer(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        return path.StartsWith(AnnotationsPath, StringComparison.Ordinal)
            ? RespondAsync(Uri.UnescapeDataString(path[AnnotationsPath.Length..]), Query(request.RequestUri), cancellationToken)
            : null;
    }

    private async Task<HttpResponseMessage> RespondAsync(string target, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken)
    {
        var inFlight = Interlocked.Increment(ref _inFlight);
        int max;

        while (inFlight > (max = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, inFlight, max) != max)
        {
        }

        try
        {
            if (Latency > TimeSpan.Zero)
            {
                await Task.Delay(Latency, cancellationToken);
            }

            var slash = target.IndexOf('/', StringComparison.Ordinal);

            if (slash < 0 && target != ResponsibilityKey && Volatile.Read(ref _gate) is { } gate)
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

            return slash < 0 ? Read(target, query) : List(target[..slash], target[(slash + 1)..], query);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private HttpResponseMessage Read(string annotationKey, IReadOnlyDictionary<string, string> query)
    {
        if (_failures.TryGetValue(annotationKey, out var failure))
        {
            return failure();
        }

        if (!_annotations.TryGetValue(annotationKey, out var json))
        {
            return StubHttpHandler.Response(HttpStatusCode.NotFound);
        }

        if (json["Deletion"] is not null && query.GetValueOrDefault("includeDeleted") != "true")
        {
            return StubHttpHandler.Response(HttpStatusCode.NotFound);
        }

        if (!IsActive(json) && query.GetValueOrDefault("includeInactive") != "true")
        {
            return StubHttpHandler.Response(HttpStatusCode.NotFound, """{ "Message": "Not active.", "ErrorCode": "AnnotationInactive" }""");
        }

        return StubHttpHandler.Response(HttpStatusCode.OK, json.ToString());
    }

    private HttpResponseMessage List(string parent, string descendants, IReadOnlyDictionary<string, string> query)
    {
        var token = query.GetValueOrDefault("continuationToken");

        if (_failures.TryGetValue($"{parent}/{descendants}#{token}", out var failure))
        {
            return failure();
        }

        var responsibility = AnnotationKey.Parse(parent);
        var type = descendants == "executions" ? AnnotationType.Execution : AnnotationType.Usage;
        var items = _annotations
            .Where(pair => !_unlisted.ContainsKey(pair.Key))
            .Select(pair => (Key: AnnotationKey.Parse(pair.Key), Json: pair.Value))
            .Where(item => item.Key.Type == type
                && item.Key.ResponsibilityName == responsibility.Name
                && item.Json["Deletion"] is null
                && IsActive(item.Json))
            .OrderByDescending(item => item.Key.ToString(), StringComparer.Ordinal)
            .Select(item => ListItem(item.Json))
            .ToList();

        var offset = Offset(descendants, token);
        var page = items.Skip(offset).Take(PageSize).ToList();
        var body = new JObject { ["Annotations"] = new JArray(page), ["Count"] = page.Count };

        if (offset + PageSize < items.Count)
        {
            body["ContinuationToken"] = Token(descendants, offset + PageSize);
        }

        return StubHttpHandler.Response(HttpStatusCode.OK, body.ToString());
    }
}
