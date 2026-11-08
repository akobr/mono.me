using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;

// The annotations of the fixture's view as the API holds them, for the informers and the catalog. The descendants
// routes of any annotation list the descendants of the segment's type by the graph, active ones unless includeInactive,
// never deleted ones, by key in descending order (pages have no order, so the informer must sort what it emits), in
// pages of PageSize with base64 tokens, as the base Annotation the API serializes them as: the discriminator stays, the
// members of the subtype (relation arrays, a unit's strings) go, and values are lossy ("list:" and the stored JSON).
// Point reads honour includeInactive and includeDeleted. Put and Update stamp Modified, as the API's writes do; Relate
// changes a relation array without, as the API's relation maintenance does. Answers on the fixture's handler before its
// routes; the server's clock is the fixture's. Counts the answers in flight, and holds the point reads of executions
// and usages on request.
internal sealed class FakeCatalog
{
    public const string Responsibility = "invoicing";
    public const string ResponsibilityKey = $"rst.{Responsibility}";
    public const string Context = "prod";

    private const string AnnotationsPath = "/api/v1/acme/billing/default/annotations/";

    // The members of the base Annotation, the only ones a list item has.
    private static readonly HashSet<string> ListedMembers = new(StringComparer.Ordinal)
    {
        "AnnotationType", "ProjectName", "ViewName", "AnnotationKey", "Name", "IsDisabled", "ValidFrom", "ExpiresAt", "TimeZone",
        "Title", "Description", "DocumentationLink", "Labels", "Values", "Created", "Modified", "Deletion",
    };

    private static readonly IReadOnlyDictionary<string, AnnotationType> Segments = new Dictionary<string, AnnotationType>(StringComparer.Ordinal)
    {
        ["usages"] = AnnotationType.Usage,
        ["contexts"] = AnnotationType.Context,
        ["units"] = AnnotationType.Unit,
        ["executions"] = AnnotationType.Execution,
        ["units-of-execution"] = AnnotationType.UnitOfExecution,
    };

    private static readonly DateTimeOffset FirstWrite = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly HostingFixture _fixture;
    private readonly ConcurrentDictionary<string, JObject> _annotations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _unlisted = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _keyGates = new(StringComparer.Ordinal);
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
        return ListPath(ResponsibilityKey, descendants);
    }

    public static string ListPath(string parent, string descendants)
    {
        return HostingFixture.AnnotationPath($"{parent}/{descendants}");
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

    // A relation array of a parent changes, and its Modified stays, as the API's relation maintenance does it.
    public void Relate(string annotationKey, string array, params string[] names)
    {
        Update(annotationKey, json => json[array] = new JArray(names.Cast<object>().ToArray()), stamp: false);
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

    // Point reads of one key wait from now until ReleaseRead(key).
    public void HoldRead(string annotationKey)
    {
        _keyGates[annotationKey] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void ReleaseRead(string annotationKey)
    {
        if (_keyGates.TryRemove(annotationKey, out var gate))
        {
            gate.TrySetResult();
        }
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

    // The page of the list at token fails; null: the first page. parent: the responsibility when null.
    public void FailPage(string descendants, string? token, HttpStatusCode status, string? json = null, string? parent = null)
    {
        _failures[$"{parent ?? ResponsibilityKey}/{descendants}#{token}"] = () => StubHttpHandler.Response(status, json);
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
        return Lists(ResponsibilityKey, descendants);
    }

    public IReadOnlyList<RecordedRequest> Lists(string parent, string descendants)
    {
        return _fixture.Http.Requests.Where(request => request.Uri.AbsolutePath == ListPath(parent, descendants)).ToList();
    }

    private static IReadOnlyDictionary<string, string> Query(Uri uri)
    {
        return uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty, StringComparer.Ordinal);
    }

    // As the list reads values through another serializer: deterministic, but not what was stored. The API serializes
    // a list item as the base Annotation, so the members of its subtype are not in it.
    private static JObject ListItem(JObject stored)
    {
        var item = (JObject)stored.DeepClone();

        foreach (var member in item.Properties().Where(member => !ListedMembers.Contains(member.Name)).ToList())
        {
            member.Remove();
        }

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

            if (slash < 0 && (_keyGates.TryGetValue(target, out var gate) || (target != ResponsibilityKey && (gate = Volatile.Read(ref _gate)) is not null)))
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

        var ancestor = AnnotationKey.Parse(parent);
        var type = Segments[descendants];
        var includeInactive = query.GetValueOrDefault("includeInactive") == "true";
        var items = _annotations
            .Where(pair => !_unlisted.ContainsKey(pair.Key))
            .Select(pair => (Key: AnnotationKey.Parse(pair.Key), Json: pair.Value))
            .Where(item => item.Key.Type == type
                && item.Key.IsDescendantOf(ancestor)
                && item.Json["Deletion"] is null
                && (includeInactive || IsActive(item.Json)))
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
