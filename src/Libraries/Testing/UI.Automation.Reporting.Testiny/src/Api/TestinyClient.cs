using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// A thin typed client of the routes of the Testiny API v1 (OpenAPI document 1.42.2) a classic test run needs. Every
// request carries the X-Api-Key header and is bounded by RequestTimeoutSeconds, an upload by the longer of that and its
// size at MinUploadBytesPerSecond. 429, 408, 5xx, a timeout and a network
// failure throw TestinyTransientException (with Testiny's Retry-After, when it sends one), any other 4xx throws
// TestinyApiException with the ApiError body. The client keeps no state; it is safe to use from several threads.
public sealed class TestinyClient
{
    public const string ApiKeyHeader = "X-Api-Key";

    // Ids per POST /testcase/find request.
    public const int MaxFindIds = 200;

    private const int MaxErrorBodyLength = 300;

    // A longer Retry-After is waited only this long, so a misbehaving proxy cannot stall the run.
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly TimeSpan requestTimeout;
    private readonly long minUploadBytesPerSecond;
    private readonly TimeProvider timeProvider;

    // The HttpClient is used as it is (no BaseAddress needed); its own Timeout should be infinite or longer than
    // RequestTimeoutSeconds.
    public TestinyClient(HttpClient httpClient, TestinyOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        this.httpClient = httpClient;
        BaseUri = options.GetBaseUri();
        apiKey = options.ApiKey;
        requestTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        minUploadBytesPerSecond = Math.Max(1, options.MinUploadBytesPerSecond);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    // The API root, with a trailing slash.
    public Uri BaseUri { get; }

    // POST /testrun: title, project_id, description (and testplan_id); the answer has the id and the _etag.
    public Task<TestinyTestRun> CreateTestRunAsync(TestinyTestRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        return SendAsync(HttpMethod.Post, "testrun", Json(run, TestinyJsonContext.Default.TestinyTestRun), TestinyJsonContext.Default.TestinyTestRun, cancellationToken);
    }

    // GET /testrun/{id}, for the current _etag.
    public Task<TestinyTestRun> GetTestRunAsync(long id, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get, Invariant($"testrun/{id}"), null, TestinyJsonContext.Default.TestinyTestRun, cancellationToken);
    }

    // PUT /testrun/{id} with the run's id, _etag and the values to change (testplan_id, description, is_closed).
    public Task<TestinyTestRun> UpdateTestRunAsync(TestinyTestRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var id = run.Id ?? throw new ArgumentException("The run to update needs its id.", nameof(run));
        return SendAsync(HttpMethod.Put, Invariant($"testrun/{id}"), Json(run, TestinyJsonContext.Default.TestinyTestRun), TestinyJsonContext.Default.TestinyTestRun, cancellationToken);
    }

    // POST /testrun/mapping/bulk/testcase:testrun?op=…: puts test cases into a run and sets their result status. The
    // OpenAPI document describes no body of the answer; it is not read.
    public async Task MapTestCasesAsync(IReadOnlyList<TestinyTestCaseRunMapping> items, TestinyMappingOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var path = $"testrun/mapping/bulk/testcase:testrun?op={Operation(operation)}";
        await SendAsync(HttpMethod.Post, path, Json(items.ToList(), TestinyJsonContext.Default.ListTestinyTestCaseRunMapping), cancellationToken);
    }

    // POST /testrun/upload-attachment/{trId}/{tcId}?title=…&filename=…&mime_type=…, the bytes as the body
    // (application/octet-stream): a file on the result of a test case in a run, within UploadTimeout of the rest of a
    // seekable content. Disposes content.
    public Task<TestinyBlob> UploadResultAttachmentAsync(
        long testRunId,
        long testCaseId,
        Stream content,
        string title,
        string fileName,
        string? mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var query = new StringBuilder()
            .Append("title=").Append(Uri.EscapeDataString(title))
            .Append("&filename=").Append(Uri.EscapeDataString(fileName));
        if (!string.IsNullOrWhiteSpace(mimeType))
        {
            query.Append("&mime_type=").Append(Uri.EscapeDataString(mimeType));
        }

        var timeout = content.CanSeek ? UploadTimeout(content.Length - content.Position) : requestTimeout;
        var body = new StreamContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return SendAsync(HttpMethod.Post, Invariant($"testrun/upload-attachment/{testRunId}/{testCaseId}?{query}"), body, TestinyJsonContext.Default.TestinyBlob, timeout, cancellationToken);
    }

    // RequestTimeoutSeconds, or longer for a file that takes longer at MinUploadBytesPerSecond.
    public TimeSpan UploadTimeout(long bytes)
    {
        var transfer = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, bytes) / (double)minUploadBytesPerSecond));
        return transfer > requestTimeout ? transfer : requestTimeout;
    }

    // POST /comment/bulk: the created comments, with their ids, in the order of the request.
    public async Task<IReadOnlyList<TestinyComment>> CreateCommentsAsync(IReadOnlyList<TestinyComment> comments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comments);
        if (comments.Count == 0)
        {
            return [];
        }

        return await SendAsync(HttpMethod.Post, "comment/bulk", Json(comments.ToList(), TestinyJsonContext.Default.ListTestinyComment), TestinyJsonContext.Default.ListTestinyComment, cancellationToken);
    }

    // POST /comment/mapping/bulk/comment:testcase:testrun?op=add: puts comments on results of test cases in a run.
    public async Task MapCommentsToResultsAsync(IReadOnlyList<TestinyCommentRunMapping> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var path = $"comment/mapping/bulk/comment:testcase:testrun?op={Operation(TestinyMappingOperation.Add)}";
        await SendAsync(HttpMethod.Post, path, Json(items.ToList(), TestinyJsonContext.Default.ListTestinyCommentRunMapping), cancellationToken);
    }

    // POST /testcase/find, MaxFindIds ids per request: which of the ids are test cases of the project (not deleted).
    public async Task<IReadOnlySet<long>> FindTestCasesAsync(long projectId, IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var found = new HashSet<long>();
        foreach (var chunk in ids.Distinct().Chunk(MaxFindIds))
        {
            var request = new TestinyFindRequest
            {
                Ids = chunk,
                Filter = new TestinyProjectFilter(projectId),
                IdOnly = true,
                OmitForbidden = true,
                Pagination = new TestinyPagination(0, chunk.Length),
            };

            var result = await SendAsync(HttpMethod.Post, "testcase/find", Json(request, TestinyJsonContext.Default.TestinyFindRequest), TestinyJsonContext.Default.TestinyFindResult, cancellationToken);
            found.UnionWith(result.Data.Select(entity => entity.Id));
        }

        return found;
    }

    private static string Operation(TestinyMappingOperation operation)
    {
        return operation switch
        {
            TestinyMappingOperation.Add => "add",
            TestinyMappingOperation.Update => "update",
            TestinyMappingOperation.AddOrUpdate => "add_or_update",
            TestinyMappingOperation.Delete => "delete",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
    }

    private static StringContent Json<T>(T value, JsonTypeInfo<T> type)
    {
        return new StringContent(JsonSerializer.Serialize(value, type), Encoding.UTF8, "application/json");
    }

    private static string Invariant(FormattableString value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static TestinyApiError? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(body, TestinyJsonContext.Default.TestinyApiError);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, JsonTypeInfo<T> responseType, CancellationToken cancellationToken)
    {
        return SendAsync(method, path, content, responseType, requestTimeout, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, JsonTypeInfo<T> responseType, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var body = await SendAsync(method, path, content, timeout, cancellationToken);

        try
        {
            return JsonSerializer.Deserialize(body, responseType)
                ?? throw new InvalidDataException($"{Describe(method, path)} answered without a body.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{Describe(method, path)} answered with a body that is not a {typeof(T).Name}: {exception.Message}", exception);
        }
    }

    private Task<string> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        return SendAsync(method, path, content, requestTimeout, cancellationToken);
    }

    // The body of a 2xx answer within timeout.
    private async Task<string> SendAsync(HttpMethod method, string path, HttpContent? content, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var operation = Describe(method, path);
        using var request = new HttpRequestMessage(method, new Uri(BaseUri.AbsoluteUri + path, UriKind.Absolute)) { Content = content };
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var deadline = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var body = await response.Content.ReadAsStringAsync(linked.Token);
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            throw Failure(operation, response, body);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TestinyTransientException($"{operation} did not answer within {timeout.TotalSeconds:0} s.", innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new TestinyTransientException($"{operation} failed: {exception.Message}", exception.StatusCode, innerException: exception);
        }
    }

    private Exception Failure(string operation, HttpResponseMessage response, string body)
    {
        var status = response.StatusCode;
        var error = TryReadError(body);
        var detail = !string.IsNullOrWhiteSpace(error?.Message)
            ? error.Message
            : string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body.Length > MaxErrorBodyLength ? body[..MaxErrorBodyLength] + "..." : body;

        var message = new StringBuilder()
            .Append(operation).Append(" answered ").Append((int)status).Append(' ').Append(response.ReasonPhrase);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message.Append(": ").Append(detail.Trim());
        }

        if (!string.IsNullOrWhiteSpace(error?.Code) || !string.IsNullOrWhiteSpace(error?.RequestId))
        {
            message.Append(" (").Append(string.Join(", ", new[] { error!.Code, error.RequestId is null ? null : $"request {error.RequestId}" }.Where(part => !string.IsNullOrWhiteSpace(part)))).Append(')');
        }

        return status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)status >= 500
            ? new TestinyTransientException(message.ToString(), status, RetryAfter(response))
            : new TestinyApiException(message.ToString(), status, error);
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta ?? (header?.Date is { } date ? date - timeProvider.GetUtcNow() : null);
        if (delay is null)
        {
            return null;
        }

        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > MaxRetryAfter ? MaxRetryAfter : delay;
    }

    // "POST testrun/mapping/bulk/testcase:testrun", without the query.
    private static string Describe(HttpMethod method, string path)
    {
        var query = path.IndexOf('?', StringComparison.Ordinal);
        return $"Testiny {method} /{(query >= 0 ? path[..query] : path)}";
    }
}
