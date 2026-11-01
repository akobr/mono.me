using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _42.Testing.UI.Automation.Reporting.Testiny.UnitTests.Fakes;

// The routes of the Testiny API the reporter uses, in memory: runs with an _etag that every update checks and renews,
// mappings, uploads, comments and POST /testcase/find over KnownCases. Every request is recorded; Fail scripts answers
// for the next matching requests.
internal sealed class FakeTestiny : HttpMessageHandler
{
    public const string BaseUrl = "https://testiny.example.test/api/v1/";

    private readonly object gate = new();
    private readonly List<RecordedRequest> requests = [];
    private readonly List<Fault> faults = [];
    private readonly Dictionary<long, JsonObject> runs = [];
    private long nextRunId = 7;
    private long nextCommentId = 100;
    private long nextBlobId = 500;
    private int nextEtag = 1;

    public HashSet<long> KnownCases { get; } = [];

    // Runs before the routes; a response it returns is the answer.
    public Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage?>>? Intercept { get; set; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

    public IReadOnlyList<long> RunIds
    {
        get
        {
            lock (gate)
            {
                return [.. runs.Keys.Order()];
            }
        }
    }

    public IReadOnlyList<RecordedRequest> To(string method, string pathPrefix)
    {
        return Requests.Where(request => request.Method == method && request.Path.StartsWith(pathPrefix, StringComparison.Ordinal)).ToArray();
    }

    // The next times matching requests get status (and body, Retry-After) instead of their answer; with afterCommit the
    // request is carried out first, as when its answer is lost.
    public void Fail(string method, string pathPrefix, HttpStatusCode status, int times = 1, string? body = null, TimeSpan? retryAfter = null, Func<RecordedRequest, bool>? when = null, bool afterCommit = false)
    {
        lock (gate)
        {
            faults.Add(new Fault(method, pathPrefix, status, body, retryAfter, when, afterCommit) { Remaining = times });
        }
    }

    // Someone else changed the run: its _etag changes.
    public void TouchRun(long id)
    {
        lock (gate)
        {
            runs[id]["_etag"] = NewEtag();
        }
    }

    public JsonObject Run(long id)
    {
        lock (gate)
        {
            return (JsonObject)runs[id].DeepClone();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recorded = await RecordAsync(request, cancellationToken);
        lock (gate)
        {
            requests.Add(recorded);
        }

        if (Intercept is not null && await Intercept(recorded, cancellationToken) is { } intercepted)
        {
            return intercepted;
        }

        lock (gate)
        {
            var fault = faults.FirstOrDefault(candidate => candidate.Remaining > 0
                && candidate.Method == recorded.Method
                && recorded.Path.StartsWith(candidate.PathPrefix, StringComparison.Ordinal)
                && (candidate.When?.Invoke(recorded) ?? true));
            if (fault is not null)
            {
                fault.Remaining--;
                if (fault.AfterCommit)
                {
                    Route(recorded).Dispose();
                }

                var response = new HttpResponseMessage(fault.Status) { Content = new StringContent(fault.Body ?? string.Empty, Encoding.UTF8, "application/json") };
                if (fault.RetryAfter is { } delay)
                {
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
                }

                return response;
            }

            return Route(recorded);
        }
    }

    private static async Task<RecordedRequest> RecordAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal) ? uri.AbsolutePath["/api/v1/".Length..] : uri.AbsolutePath;
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(part => Uri.UnescapeDataString(part[0]), part => part.Length > 1 ? Uri.UnescapeDataString(part[1]) : string.Empty, StringComparer.Ordinal);
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        return new RecordedRequest(request.Method.Method, Uri.UnescapeDataString(path), query, headers, request.Content?.Headers.ContentType?.MediaType, body);
    }

    private static HttpResponseMessage Json(JsonNode node, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    private HttpResponseMessage Route(RecordedRequest request)
    {
        var segments = request.Path.Split('/');
        switch (request.Method, segments)
        {
            case ("POST", ["testrun"]):
            {
                var run = (JsonObject)JsonNode.Parse(request.Body)!;
                var id = nextRunId++;
                run["id"] = id;
                run["is_closed"] = false;
                run["_etag"] = NewEtag();
                runs[id] = run;
                return Json(run.DeepClone());
            }

            case ("GET", ["testrun", var id]):
                return runs.TryGetValue(long.Parse(id, CultureInfo.InvariantCulture), out var found)
                    ? Json(found.DeepClone())
                    : Json(new JsonObject { ["message"] = "Not found" }, HttpStatusCode.NotFound);

            case ("PUT", ["testrun", var id]):
            {
                var run = runs[long.Parse(id, CultureInfo.InvariantCulture)];
                var update = (JsonObject)JsonNode.Parse(request.Body)!;
                if (update["_etag"]?.GetValue<string>() != run["_etag"]!.GetValue<string>())
                {
                    return Json(new JsonObject { ["code"] = "API_UPDATE_CONFLICT", ["status"] = 409, ["message"] = "Update conflict: The entity was already modified elsewhere." }, HttpStatusCode.Conflict);
                }

                foreach (var (key, value) in update)
                {
                    run[key] = value?.DeepClone();
                }

                run["_etag"] = NewEtag();
                return Json(run.DeepClone());
            }

            case ("POST", ["testrun", "mapping", "bulk", "testcase:testrun"]):
            case ("POST", ["comment", "mapping", "bulk", "comment:testcase:testrun"]):
                return Json(new JsonArray());

            case ("POST", ["testrun", "upload-attachment", _, _]):
                return Json(new JsonObject
                {
                    ["id"] = nextBlobId++,
                    ["title"] = request.Query["title"],
                    ["filename"] = request.Query["filename"],
                    ["mime_type"] = request.Query.GetValueOrDefault("mime_type"),
                    ["size"] = request.Body.Length,
                });

            case ("POST", ["comment", "bulk"]):
            {
                var created = new JsonArray();
                foreach (var comment in JsonNode.Parse(request.Body)!.AsArray())
                {
                    var copy = (JsonObject)comment!.DeepClone();
                    copy["id"] = nextCommentId++;
                    created.Add(copy);
                }

                return Json(created);
            }

            case ("POST", ["testrun", "find"]):
            {
                var filter = request.Json.GetProperty("filter");
                var projectId = filter.GetProperty("project_id").GetInt64();
                var title = filter.GetProperty("title").GetString();
                var data = new JsonArray(runs.Values
                    .Where(run => run["project_id"]?.GetValue<long>() == projectId && run["title"]?.GetValue<string>() == title)
                    .OrderByDescending(run => run["id"]!.GetValue<long>())
                    .Select(run => (JsonNode)run.DeepClone())
                    .ToArray());
                return Json(new JsonObject { ["data"] = data, ["meta"] = new JsonObject { ["count"] = data.Count, ["offset"] = 0 } });
            }

            case ("POST", ["testcase", "find"]):
            {
                var ids = request.Json.GetProperty("ids").EnumerateArray().Select(id => id.GetInt64()).Where(KnownCases.Contains).ToArray();
                var data = new JsonArray(ids.Select(id => (JsonNode)new JsonObject { ["id"] = id }).ToArray());
                return Json(new JsonObject { ["data"] = data, ["meta"] = new JsonObject { ["count"] = ids.Length, ["offset"] = 0 } });
            }

            default:
                return Json(new JsonObject { ["message"] = $"No route for {request.Method} {request.Path}" }, HttpStatusCode.NotFound);
        }
    }

    private string NewEtag()
    {
        return string.Create(CultureInfo.InvariantCulture, $"e{nextEtag++}");
    }

    private sealed record Fault(string Method, string PathPrefix, HttpStatusCode Status, string? Body, TimeSpan? RetryAfter, Func<RecordedRequest, bool>? When, bool AfterCommit)
    {
        public int Remaining { get; set; }
    }
}
