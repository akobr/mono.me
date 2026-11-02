using _42.Platform.UI.Admin.AutomationTests.Support;

namespace _42.Platform.UI.Admin.AutomationTests;

// The Annotations section's checks of the request recorder (InstallRequestRecorderAsync): empty it right before a step,
// then check what the step sent, e.g. that a Cancel or a refused Save wrote nothing.
public abstract partial class PlatformTest
{
    // Drops the fetches recorded so far, so the next check sees only what follows.
    protected async Task ClearRecordedRequestsAsync()
    {
        await Page.EvaluateAsync("() => { if (window.__requests) { window.__requests.length = 0; } }");
    }

    // The recorder holds a request that matches; what names it in the failure.
    protected async Task ExpectRequestAsync(Func<RecordedRequest, bool> match, string what)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        var requests = await RecordedRequestsAsync();
        if (!requests.Any(match))
        {
            Assert.Fail($"The page should have sent {what}; it sent {Describe(requests)}.");
        }
    }

    // The recorder holds no request that matches; what names it in the failure.
    protected async Task ExpectNoRequestAsync(Func<RecordedRequest, bool> match, string what)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        var requests = await RecordedRequestsAsync();
        var matching = requests.Where(match).ToList();
        if (matching.Count > 0)
        {
            Assert.Fail($"The page should not have sent {what}, but it sent {Describe(matching)}.");
        }
    }

    // The lookup of deleted keys that a create sends before it writes (POST …/annotations/deleted/lookup).
    protected static bool IsDeletedKeyLookup(RecordedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Method == "POST" && PathOf(request).EndsWith("/annotations/deleted/lookup", StringComparison.Ordinal);
    }

    // A restore of deleted keys (…/annotations/deleted/restore).
    protected static bool IsDeletedKeyRestore(RecordedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PathOf(request).EndsWith("/annotations/deleted/restore", StringComparison.Ordinal);
    }

    // A bulk create (…/annotations/simple).
    protected static bool IsBulkCreate(RecordedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PathOf(request).EndsWith("/annotations/simple", StringComparison.Ordinal);
    }

    // A write of the annotation: any method but GET of …/annotations/<key> (SetAnnotation is its POST) or below it.
    protected static Func<RecordedRequest, bool> IsWriteOf(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = $"/annotations/{Uri.EscapeDataString(key)}";
        return request => request.Method != "GET"
            && (PathOf(request).EndsWith(path, StringComparison.Ordinal) || PathOf(request).Contains($"{path}/", StringComparison.Ordinal));
    }

    private static string PathOf(RecordedRequest request)
    {
        return Uri.TryCreate(request.Url, UriKind.Absolute, out var url) ? url.AbsolutePath : request.Url;
    }

    private static string Describe(IReadOnlyCollection<RecordedRequest> requests)
    {
        return requests.Count == 0 ? "nothing" : string.Join(", ", requests.Select(request => $"{request.Method} {request.Url}"));
    }
}
