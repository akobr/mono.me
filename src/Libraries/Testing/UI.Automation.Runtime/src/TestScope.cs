using System.Collections.Concurrent;
using System.Globalization;
using _42.Testing.UI.Automation.Runtime.Artifacts;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Data;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime;

// What one attempt of one test gets, and everything the engine hooks only delegate to. Create never touches the
// browser; BeginAsync leases the user, acquires the browser, creates the context (signed in from the cached storage
// state), starts tracing, captures the console, page errors and failed requests, and opens the page. EndAsync always
// works, also after a BeginAsync that threw: it keeps the screenshot, trace and video per the Execution policies,
// hands the refreshed sign-in of every signed-in context back to the cache (or drops it when the context shows the
// user signed out), writes console.log, closes the contexts, releases the browser, returns the users and hands the
// report to the dispatcher. It never throws; a cleanup step that fails is logged.
//
// The cached sign-in is never redone on the test's page: that page is traced, and a trace keeps the parameters of
// every action, the typed password included. A test that signs in itself (TestScopeOptions.SignedIn false) has that
// exposure in its trace.zip; turn its trace off (Execution:Trace Off) when it types a real password.
public sealed class TestScope : IAsyncDisposable
{
    private readonly TestScopeOptions options;
    private readonly ConcurrentQueue<string> consoleLines = new();
    private readonly ConcurrentDictionary<string, string> properties = new(StringComparer.Ordinal);
    private readonly List<TestUser> extraUsers = [];
    private readonly List<IBrowserContext> extraContexts = [];
    private readonly List<(IBrowserContext Context, TestUser User)> signedInContexts = [];
    private readonly object gate = new();
    private TestUser? user;
    private BrowserLease? lease;
    private IBrowserContext? context;
    private IPage? page;
    private string? videoDirectory;
    private bool tracing;
    private int begun;
    private int contextsClosed;
    private int released;
    private Task<TestResultReport>? ending;

    private TestScope(AutomationSession session, TestIdentity test, TestScopeOptions options)
    {
        this.options = options;
        Session = session;
        Test = test;
        Attempt = options.Attempt;
        StartedAt = options.StartedAt ?? session.TimeProvider.GetUtcNow();
        Data = session.CreateData(test, options.Attempt);
        Artifacts = session.CreateArtifacts(test, options.Attempt);
    }

    public AutomationSession Session { get; }

    public TestIdentity Test { get; }

    public int Attempt { get; }

    public DateTimeOffset StartedAt { get; }

    public SeededData Data { get; }

    public ArtifactCollector Artifacts { get; }

    // What BeginAsync threw; EndAsync reports it when the engine passes no exception of its own.
    public Exception? BeginException { get; private set; }

    public bool IsStarted => page is not null;

    public BrowserLease Browser => lease ?? throw NotStarted();

    public IBrowserContext Context => context ?? throw NotStarted();

    public IPage Page => page ?? throw NotStarted();

    public bool HasUser => user is not null;

    public TestUser User => user ?? throw new InvalidOperationException("The test leased no user (TestScopeOptions.LeaseUser is false).");

    // The users of LeaseAnotherUserAsync; returned at the end.
    public IReadOnlyList<TestUser> ExtraUsers
    {
        get
        {
            lock (gate)
            {
                return extraUsers.ToArray();
            }
        }
    }

    // Added to the report's properties, e.g. the keys the test created.
    public IDictionary<string, string> Properties => properties;

    public static TestScope Create(AutomationSession session, TestIdentity test, TestScopeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(test);

        options ??= new TestScopeOptions();
        if (options.SignedIn && !options.LeaseUser)
        {
            throw new ArgumentException("TestScopeOptions.SignedIn needs LeaseUser.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.Attempt, 1, nameof(options));
        return new TestScope(session, test, options);
    }

    // Once per scope. On failure the exception is kept in BeginException and rethrown; call EndAsync anyway.
    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref begun, 1) != 0)
        {
            throw new InvalidOperationException("BeginAsync runs once per scope.");
        }

        try
        {
            if (options.LeaseUser)
            {
                user = await Session.Users.LeaseAsync(options.User, cancellationToken);
            }

            lease = await Session.BrowserProvider.AcquireAsync(Test, cancellationToken);

            string? storageState = null;
            if (options.SignedIn && Session.StorageStates is { } cache)
            {
                storageState = await cache.GetAsync(user!, lease.Browser, Session.ContextOptions.Create(), Artifacts, cancellationToken);
            }

            var execution = Session.Settings.Execution;
            if (execution.Video != ArtifactPolicy.Off)
            {
                videoDirectory = Artifacts.GetPath(".video");
            }

            context = await NewContextCoreAsync(storageState, videoDirectory, label: null);
            if (storageState is not null)
            {
                lock (gate)
                {
                    signedInContexts.Add((context, user!));
                }
            }

            if (execution.Trace != ArtifactPolicy.Off)
            {
                await context.Tracing.StartAsync(new TracingStartOptions
                {
                    Title = Test.DisplayName,
                    Screenshots = true,
                    Snapshots = true,
                    Sources = true,
                });
                tracing = true;
            }

            page = await context.NewPageAsync();
        }
        catch (Exception exception)
        {
            BeginException = exception;
            throw;
        }
    }

    // A second user for the test (e.g. one who accepts an invitation); returned at the end.
    public async Task<TestUser> LeaseAnotherUserAsync(UserRequirement requirement, CancellationToken cancellationToken = default)
    {
        var other = await Session.Users.LeaseAsync(requirement, cancellationToken);
        lock (gate)
        {
            extraUsers.Add(other);
        }

        return other;
    }

    // Another context of the test's browser, signed in as asUser when given (and a sign-in flow is set); closed at the end.
    // Its console goes to the same console.log; it is not traced.
    public async Task<IBrowserContext> NewContextAsync(TestUser? asUser = null, CancellationToken cancellationToken = default)
    {
        var browserLease = Browser;
        string? storageState = null;
        if (asUser is not null && Session.StorageStates is { } cache)
        {
            storageState = await cache.GetAsync(asUser, browserLease.Browser, Session.ContextOptions.Create(), Artifacts, cancellationToken);
        }

        int number;
        lock (gate)
        {
            number = extraContexts.Count + 2;
        }

        var created = await NewContextCoreAsync(storageState, null, asUser?.Fingerprint ?? $"context-{number}");
        lock (gate)
        {
            extraContexts.Add(created);
            if (storageState is not null)
            {
                signedInContexts.Add((created, asUser!));
            }
        }

        return created;
    }

    // Once; later calls return the same report. outcome: the engine's verdict (a failure from a missing user or browser
    // becomes Blocked); exception: what failed the test, BeginException when null; output: the test's captured output;
    // report: false for an attempt the engine runs again, whose report only goes back to the caller (the artifacts stay).
    public Task<TestResultReport> EndAsync(TestOutcome outcome, Exception? exception = null, string? output = null, bool report = true)
    {
        lock (gate)
        {
            return ending ??= EndCoreAsync(outcome, exception ?? BeginException, output, report);
        }
    }

    // Cleans up without a report when EndAsync was not called.
    public async ValueTask DisposeAsync()
    {
        Task<TestResultReport>? current;
        lock (gate)
        {
            current = ending;
        }

        if (current is not null)
        {
            await current;
            return;
        }

        await CloseContextsAsync();
        await ReleaseAsync(null);
    }

    private static bool Keeps(ArtifactPolicy policy, bool failed)
    {
        return policy == ArtifactPolicy.Always || (policy == ArtifactPolicy.OnFailure && failed);
    }

    private static InvalidOperationException NotStarted()
    {
        return new InvalidOperationException("The test scope has not begun (BeginAsync) or its start failed.");
    }

    private async Task<IBrowserContext> NewContextCoreAsync(string? storageState, string? recordVideoDirectory, string? label)
    {
        var execution = Session.Settings.Execution;
        var created = await Browser.Browser.NewContextAsync(Session.ContextOptions.Create(storageState, recordVideoDirectory));
        created.SetDefaultTimeout(execution.ActionTimeoutMs);
        created.SetDefaultNavigationTimeout(execution.NavigationTimeoutMs);

        var prefix = label is null ? string.Empty : $"[{label}] ";
        created.Console += (_, message) => Log($"{prefix}[{message.Type}] {message.Text}{(string.IsNullOrEmpty(message.Location) ? string.Empty : $" ({message.Location})")}");
        created.WebError += (_, error) => Log($"{prefix}[pageerror] {error.Error}");
        created.RequestFailed += (_, request) => Log($"{prefix}[requestfailed] {request.Method} {request.Url}: {request.Failure}");
        return created;
    }

    private void Log(string line)
    {
        consoleLines.Enqueue($"{Session.TimeProvider.GetUtcNow().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {line}");
    }

    private async Task<TestResultReport> EndCoreAsync(TestOutcome outcome, Exception? exception, string? output, bool reportResult)
    {
        outcome = AutomationOutcomes.Refine(outcome, exception);
        var failed = AutomationOutcomes.KeepsFailureArtifacts(outcome);
        var execution = Session.Settings.Execution;
        var duration = Session.TimeProvider.GetUtcNow() - StartedAt;

        if (page is { } currentPage && Keeps(execution.Screenshot, failed))
        {
            await TryAsync("the screenshot", async () =>
            {
                var path = Artifacts.GetPath(failed ? "failure.png" : "final.png");
                await currentPage.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true, Timeout = 10_000 });
                Artifacts.Add(path, ArtifactKind.Screenshot);
            });
        }

        if (tracing && context is { } tracedContext)
        {
            await TryAsync("the trace", async () =>
            {
                if (Keeps(execution.Trace, failed))
                {
                    var path = Artifacts.GetPath("trace.zip");
                    await tracedContext.Tracing.StopAsync(new TracingStopOptions { Path = path });
                    Artifacts.Add(path, ArtifactKind.Trace);
                }
                else
                {
                    await tracedContext.Tracing.StopAsync();
                }
            });
        }

        // After the trace stopped, so reading the storage state is not one of the test's traced actions.
        await KeepSignInsAsync();
        await CloseContextsAsync();
        await KeepVideoAsync(Keeps(execution.Video, failed));

        if (!consoleLines.IsEmpty)
        {
            await TryAsync("console.log", () => Artifacts.WriteTextAsync("console.log", string.Join('\n', consoleLines) + "\n", ArtifactKind.ConsoleLog, CancellationToken.None));
        }

        var values = new Dictionary<string, string>(lease?.Properties ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        if (user is not null)
        {
            values[TestRunProperties.User] = user.Fingerprint;
        }

        if (lease?.RemoteSessionName is { } remoteSession)
        {
            values[TestRunProperties.RemoteSession] = remoteSession;
        }

        foreach (var (key, value) in properties)
        {
            values[key] = value;
        }

        var report = Session.CreateReport(Test, outcome, StartedAt, duration, Attempt, exception, output, Artifacts.Artifacts, values);
        await ReleaseAsync(report);
        if (reportResult)
        {
            Session.Report(report);
        }

        return report;
    }

    // Every context that started signed in from the cache, whatever the outcome: one that shows the user signed out drops
    // the user's cached state (so a test that signs out on purpose leaves no dead state behind); one that is still
    // signed in hands its refreshed sign-in to the cache, because identity providers that rotate refresh tokens retire
    // the cached token at the test's first page load. A refresh that fails drops the state too: it may hold a token the
    // test used up.
    private async Task KeepSignInsAsync()
    {
        if (Session.StorageStates is not { } cache)
        {
            return;
        }

        (IBrowserContext Context, TestUser User)[] contexts;
        lock (gate)
        {
            contexts = [.. signedInContexts];
        }

        foreach (var (signedIn, signedInUser) in contexts)
        {
            await TryAsync("keeping the sign-in", async () =>
            {
                var shown = ReferenceEquals(signedIn, context) ? page : signedIn.Pages.Count > 0 ? signedIn.Pages[0] : null;
                if (shown is not null && await cache.Flow.IsSignedOutAsync(shown, CancellationToken.None))
                {
                    cache.Invalidate(signedInUser);
                    return;
                }

                try
                {
                    await cache.RefreshAsync(signedInUser, signedIn);
                }
                catch
                {
                    cache.Invalidate(signedInUser);
                    throw;
                }
            });
        }
    }

    private async Task KeepVideoAsync(bool keep)
    {
        if (videoDirectory is null)
        {
            return;
        }

        if (page?.Video is { } video)
        {
            await TryAsync("the video", async () =>
            {
                if (keep)
                {
                    var path = Artifacts.GetPath("video.webm");
                    await video.SaveAsAsync(path);
                    Artifacts.Add(path, ArtifactKind.Video);
                }

                await video.DeleteAsync();
            });
        }

        await TryAsync("the raw video directory", () =>
        {
            if (Session.FileSystem.Directory.Exists(videoDirectory))
            {
                Session.FileSystem.Directory.Delete(videoDirectory, recursive: true);
            }

            return Task.CompletedTask;
        });
    }

    private async Task CloseContextsAsync()
    {
        if (Interlocked.Exchange(ref contextsClosed, 1) != 0)
        {
            return;
        }

        IBrowserContext[] contexts;
        lock (gate)
        {
            contexts = [.. extraContexts];
        }

        foreach (var extra in contexts)
        {
            await TryAsync("closing a context", () => extra.CloseAsync());
        }

        if (context is { } main)
        {
            await TryAsync("closing the context", () => main.CloseAsync());
        }
    }

    private async Task ReleaseAsync(TestResultReport? report)
    {
        if (Interlocked.Exchange(ref released, 1) != 0)
        {
            return;
        }

        if (lease is { } current)
        {
            await TryAsync("releasing the browser", () => Session.BrowserProvider.ReleaseAsync(current, report, CancellationToken.None).AsTask());
        }

        if (user is not null)
        {
            Session.Users.Return(user);
        }

        foreach (var other in ExtraUsers)
        {
            Session.Users.Return(other);
        }
    }

    private async Task TryAsync(string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Session.Logger.LogWarning(exception, "{Test}: {Step} failed.", Test.DisplayName, step);
        }
    }
}
