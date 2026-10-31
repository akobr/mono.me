using System.Collections.Concurrent;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// Browsers of BrowserStack's grid (or of any Playwright server at BrowserStack:Endpoint), connected through the
// installed Playwright driver with the BrowserType of BrowserStack:Browser's engine and the session's capabilities in
// the caps parameter. SessionPerTest (the default): a session per test, named <Class>.<Method>, connected by
// AcquireAsync, marked passed or failed and closed by ReleaseAsync. Otherwise a session per test class, named <Class>,
// which its tests share (each in its own context), marked from the last attempt of each of them. It closes once no test
// holds it and every planned test of the class is done: it released a browser, or its final report is known
// (TestCompleted, also for a test an attribute skipped or one that ended without a browser). A session that no test
// holds while tests of its class are still to come (a [Serial] one, a retry, a test that never starts) closes after
// IdleCloseAfter, before BrowserStack's idle timeout would end it unmarked, and the class's next test opens another;
// one that BrowserStack ended is replaced, and closes when its last test leaves it. What is still open closes when the
// provider is disposed. The mark goes through a page of the session, a new one when the tests' contexts are closed.
// Nothing connects at start: the first test shows wrong credentials. The access key travels in the caps URL, so every
// message and exception of the provider shows it as ***.
public sealed class BrowserStackBrowserProvider : IBrowserProvider
{
    private const string VersionTable = "https://www.browserstack.com/docs/automate/playwright/browsers-and-os";

    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);

    private readonly BrowserStackSettings settings;
    private readonly IRemoteBrowserConnector connector;
    private readonly ILogger logger;
    private readonly TimeProvider timeProvider;
    private readonly SecretRedactor redactor;
    private readonly BrowserEngine engine;
    private readonly float? slowMo;
    private readonly Dictionary<string, HashSet<string>> plannedByClass;
    private readonly ConcurrentDictionary<BrowserLease, Holder> leases = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RemoteSession> classSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> doneByClass = new(StringComparer.Ordinal);
    private readonly List<Task> closing = [];
    private readonly object gate = new();
    private bool disposed;

    internal BrowserStackBrowserProvider(BrowserProviderContext context, IRemoteBrowserConnector connector)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(connector);

        settings = context.Settings.BrowserStack;
        this.connector = connector;
        logger = context.Logger;
        timeProvider = context.TimeProvider;
        redactor = new SecretRedactor(settings.AccessKey);
        engine = context.Settings.EffectiveEngine;
        slowMo = context.Settings.Browser.SlowMoMs > 0 ? context.Settings.Browser.SlowMoMs : null;
        Build = BrowserStackCapabilities.ResolveBuild(settings, context.Run);
        IdleCloseAfter = BrowserStackCapabilities.IdleTimeout(settings) * 2 / 3;
        plannedByClass = context.PlannedTests
            .GroupBy(test => test.ClassName, StringComparer.Ordinal)
            .ToDictionary(tests => tests.Key, tests => tests.Select(test => test.Id).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        var values = new Dictionary<string, string>
        {
            [TestRunProperties.Provider] = BrowserProviderKind.BrowserStack.ToString(),
            [TestRunProperties.Engine] = engine.ToString().ToLowerInvariant(),
            [TestRunProperties.Channel] = settings.GetBrowserName(),
            [TestRunProperties.Os] = $"{settings.Os} {settings.OsVersion}",
        };

        // The version asked for (latest, latest-1, …); a lease has the version of its browser.
        if (settings.GetRequestedBrowserVersion() is { } version)
        {
            values[TestRunProperties.BrowserVersion] = version;
        }

        Properties = values;
    }

    public BrowserProviderKind Kind => BrowserProviderKind.BrowserStack;

    public int? MaxParallelSessions => settings.MaxParallelSessions;

    public DeviceCatalog Devices => connector.Devices;

    public IReadOnlyDictionary<string, string> Properties { get; }

    // The build of the sessions: BrowserStack:Build with the run's placeholders, or its default.
    public string Build { get; }

    // A session per class only: how long a session that no test holds stays open while tests of its class are still to
    // come; two thirds of BrowserStack's idle timeout.
    public TimeSpan IdleCloseAfter { get; }

    // Starts the Playwright driver; connects nothing yet.
    public static async Task<BrowserStackBrowserProvider> StartAsync(BrowserProviderContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        PlaywrightBrowserConnector connector;
        try
        {
            connector = await PlaywrightBrowserConnector.StartAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new BrowserUnavailableException($"The Playwright driver did not start: {exception.Message}", exception);
        }

        var provider = new BrowserStackBrowserProvider(context, connector);
        provider.LogStart();
        return provider;
    }

    // Throws BrowserUnavailableException when the session does not start, with the access key as ***.
    public async ValueTask<BrowserLease> AcquireAsync(TestIdentity test, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);
        ObjectDisposedException.ThrowIf(disposed, this);

        var (session, browser) = settings.SessionPerTest
            ? await ConnectForTestAsync(test, cancellationToken)
            : await JoinClassSessionAsync(test, cancellationToken);

        var properties = new Dictionary<string, string>(Properties)
        {
            [TestRunProperties.BrowserVersion] = browser.Version,
        };

        var lease = new BrowserLease(browser, session.Name, properties);
        leases[lease] = new Holder(test, session);
        return lease;
    }

    // Never throws: a mark or a close that fails is a warning.
    public async ValueTask ReleaseAsync(BrowserLease lease, TestResultReport? result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (!leases.TryRemove(lease, out var holder))
        {
            return;
        }

        var session = holder.Session;
        if (settings.SessionPerTest)
        {
            await CloseAsync(session, BrowserStackSessionStatus.ForTest(result, redactor.Redact));
            return;
        }

        Task[] ended;
        lock (gate)
        {
            session.Active--;
            if (result is not null)
            {
                session.Results[holder.Test.Id] = result;
            }

            MarkDone(holder.Test);
            ended = CloseIdleSessions(holder.Test.ClassName, session);
        }

        await Task.WhenAll(ended);
    }

    // A session per class only: the test is done for the session of its class, whether it held it or not (an attribute
    // skipped it, it was blocked before its browser, or its connection failed). Never throws.
    public void TestCompleted(TestIdentity test)
    {
        ArgumentNullException.ThrowIfNull(test);

        if (settings.SessionPerTest)
        {
            return;
        }

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            MarkDone(test);
            CloseIdleSessions(test.ClassName, left: null);
        }
    }

    // Marks and closes every session still open (a class session from the results it has, an unreleased test's
    // session unmarked), then stops the driver.
    public async ValueTask DisposeAsync()
    {
        List<(RemoteSession Session, BrowserStackSessionStatus? Status)> open = [];
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (var session in classSessions.Values)
            {
                session.CancelIdleClose();
                open.Add((session, StatusOf(session)));
            }

            classSessions.Clear();
        }

        var known = new HashSet<RemoteSession>(open.Select(entry => entry.Session), ReferenceEqualityComparer.Instance);
        foreach (var holder in leases.Values)
        {
            if (known.Add(holder.Session))
            {
                open.Add((holder.Session, null));
            }
        }

        leases.Clear();
        foreach (var (session, status) in open)
        {
            _ = CloseAsync(session, status);
        }

        Task[] pending;
        lock (gate)
        {
            pending = [.. closing];
        }

        await Task.WhenAll(pending);
        await connector.DisposeAsync();
    }

    private async Task<(RemoteSession Session, IBrowser Browser)> ConnectForTestAsync(TestIdentity test, CancellationToken cancellationToken)
    {
        var name = BrowserStackCapabilities.SessionName(test, perTest: true);
        var session = new RemoteSession(name, ConnectAsync(name, cancellationToken));
        return (session, await session.Connecting);
    }

    private async Task<(RemoteSession Session, IBrowser Browser)> JoinClassSessionAsync(TestIdentity test, CancellationToken cancellationToken)
    {
        var className = test.ClassName;
        for (var attempt = 1; ; attempt++)
        {
            RemoteSession? session;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!classSessions.TryGetValue(className, out session))
                {
                    // Shared by the tests of the class, so no test's cancellation stops it.
                    var name = BrowserStackCapabilities.SessionName(test, perTest: false);
                    session = new RemoteSession(name, Task.Run(() => ConnectAsync(name, CancellationToken.None), CancellationToken.None));
                    classSessions[className] = session;
                }

                session.Active++;
                session.Joins++;
                session.CancelIdleClose();
            }

            IBrowser browser;
            try
            {
                browser = await session.Connecting.WaitAsync(cancellationToken);
            }
            catch
            {
                // A session that did not start is dropped, so the next test of the class connects again.
                Leave(className, session, drop: session.Connecting.IsFaulted);
                throw;
            }

            if (browser.IsConnected || attempt > 1)
            {
                return (session, browser);
            }

            logger.LogWarning("The BrowserStack session {Name} has ended (e.g. its idle timeout); the class gets a new one.", session.Name);
            Leave(className, session, drop: true);
        }
    }

    private void Leave(string className, RemoteSession session, bool drop)
    {
        lock (gate)
        {
            session.Active--;
            if (drop && IsCurrent(className, session))
            {
                classSessions.Remove(className);
            }

            CloseIdleSessions(className, session);
        }
    }

    // Under the gate, after a test of the class was done or left a session: starts the closes that are due and returns
    // them. A session that was replaced closes when its last test leaves it. The class's session closes when no test
    // holds it and every planned test of the class is done; while tests of the class are still to come, the session
    // that its last test left closes after IdleCloseAfter, unless a test joins it before.
    private Task[] CloseIdleSessions(string className, RemoteSession? left)
    {
        List<Task> ended = [];
        if (left is { Active: 0 } && !IsCurrent(className, left))
        {
            ended.Add(CloseAsync(left, StatusOf(left)));
        }

        if (classSessions.TryGetValue(className, out var current) && current.Active == 0)
        {
            if (IsComplete(className))
            {
                classSessions.Remove(className);
                current.CancelIdleClose();
                ended.Add(CloseAsync(current, StatusOf(current)));
            }
            else if (ReferenceEquals(current, left))
            {
                current.CancelIdleClose();
                var joins = current.Joins;
                current.IdleClose = timeProvider.CreateTimer(_ => CloseIdle(className, current, joins), null, IdleCloseAfter, Timeout.InfiniteTimeSpan);
            }
        }

        return [.. ended];
    }

    // The idle timer of a class session: closes it unless a test joined it since.
    private void CloseIdle(string className, RemoteSession session, int joins)
    {
        lock (gate)
        {
            if (disposed || session.Active > 0 || session.Joins != joins || !IsCurrent(className, session))
            {
                return;
            }

            classSessions.Remove(className);
            session.CancelIdleClose();
            logger.LogInformation(
                "The BrowserStack session {Name} was idle for {Seconds:0} s with tests of its class still to come; it is closed, and the next of them opens another.",
                session.Name,
                IdleCloseAfter.TotalSeconds);
            CloseAsync(session, StatusOf(session));
        }
    }

    private void MarkDone(TestIdentity test)
    {
        if (!doneByClass.TryGetValue(test.ClassName, out var done))
        {
            done = new HashSet<string>(StringComparer.Ordinal);
            doneByClass[test.ClassName] = done;
        }

        done.Add(test.Id);
    }

    // Every planned test of the class is done; never for a class without planned tests.
    private bool IsComplete(string className)
    {
        return plannedByClass.TryGetValue(className, out var planned)
            && doneByClass.TryGetValue(className, out var done)
            && planned.IsSubsetOf(done);
    }

    private bool IsCurrent(string className, RemoteSession session)
    {
        return classSessions.TryGetValue(className, out var current) && ReferenceEquals(current, session);
    }

    private BrowserStackSessionStatus? StatusOf(RemoteSession session)
    {
        return BrowserStackSessionStatus.ForSession([.. session.Results.Values], redactor.Redact);
    }

    private async Task<IBrowser> ConnectAsync(string name, CancellationToken cancellationToken)
    {
        var capabilities = BrowserStackCapabilities.Create(settings, name, Build, AutomationSession.PlaywrightVersion);
        var endpoint = BrowserStackCapabilities.CreateEndpoint(settings.Endpoint, BrowserStackCapabilities.ToJson(capabilities));
        var options = new BrowserTypeConnectOptions
        {
            Timeout = settings.ConnectTimeoutSeconds * 1000f,
            SlowMo = slowMo,
        };

        try
        {
            return await connector.ConnectAsync(engine, endpoint, options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Not the exception itself, which may hold the caps URL: only its type and its redacted message.
            throw new BrowserUnavailableException(
                $"The BrowserStack session '{name}' did not start at {redactor.Redact(BrowserStackCapabilities.Describe(settings.Endpoint))}: {VersionHint(exception)}{Describe(exception)}");
        }
    }

    private Task CloseAsync(RemoteSession session, BrowserStackSessionStatus? status)
    {
        var task = CloseCoreAsync(session, status);
        lock (gate)
        {
            closing.Add(task);
        }

        return task;
    }

    // Yields first, so a close started under the gate does none of its work there.
    private async Task CloseCoreAsync(RemoteSession session, BrowserStackSessionStatus? status)
    {
        await Task.Yield();

        IBrowser browser;
        try
        {
            browser = await session.Connecting;
        }
        catch (Exception)
        {
            // It never started; the test that waited for it reported why.
            return;
        }

        if (status is not null && browser.IsConnected)
        {
            try
            {
                await MarkAsync(browser, status);
            }
            catch (Exception exception)
            {
                logger.LogWarning("The BrowserStack session {Name} was not marked {Status}: {Error}", session.Name, status.Status, Describe(exception));
            }
        }

        try
        {
            await browser.CloseAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning("The BrowserStack session {Name} did not close cleanly: {Error}", session.Name, Describe(exception));
        }
    }

    private static async Task MarkAsync(IBrowser browser, BrowserStackSessionStatus status)
    {
        var page = browser.Contexts.SelectMany(context => context.Pages).FirstOrDefault(candidate => !candidate.IsClosed);
        if (page is null)
        {
            var context = await browser.NewContextAsync();
            page = await context.NewPageAsync();
        }

        await page.EvaluateAsync(BrowserStackSessionStatus.Expression, status.Payload).WaitAsync(StatusTimeout);
    }

    // Playwright's server refuses a client whose major.minor version is not its own (HTTP 428, "Playwright version
    // mismatch"), and BrowserStack runs only the versions of its table.
    private static string VersionHint(Exception exception)
    {
        var message = exception.Message;
        if (!message.Contains("version mismatch", StringComparison.OrdinalIgnoreCase) && !message.Contains("428 Precondition Required", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return $"the server does not run the Playwright version of this client, {AutomationSession.PlaywrightVersion} (both sides need the same major.minor); "
            + $"pin Microsoft.Playwright to a version BrowserStack lists ({VersionTable}) and leave BrowserStack:PlaywrightVersion unset or set it to the client's. ";
    }

    private string Describe(Exception exception)
    {
        return $"{exception.GetType().Name}: {redactor.Redact(exception.Message)}";
    }

    private void LogStart()
    {
        logger.LogInformation(
            "BrowserStack: build '{Build}', {Browser} {BrowserVersion} on {Os} {OsVersion}{Local}, a session per {Scope}, at most {Sessions} parallel sessions, Playwright {Client} (BrowserStack: {Server}), {Endpoint}.",
            Build,
            settings.GetBrowserName(),
            settings.GetRequestedBrowserVersion() ?? (settings.UsesBrowserVersion() ? "(BrowserStack's default version)" : "(its Playwright build)"),
            settings.Os,
            settings.OsVersion,
            settings.Local ? $", BrowserStack Local {settings.LocalIdentifier}".TrimEnd() : string.Empty,
            settings.SessionPerTest ? "test" : $"test class (closed after {IdleCloseAfter.TotalSeconds:0} s without a test)",
            settings.MaxParallelSessions,
            AutomationSession.PlaywrightVersion,
            settings.PlaywrightVersion ?? "the client's",
            redactor.Redact(BrowserStackCapabilities.Describe(settings.Endpoint)));
    }

    private sealed record Holder(TestIdentity Test, RemoteSession Session);

    private sealed class RemoteSession(string name, Task<IBrowser> connecting)
    {
        public string Name { get; } = name;

        public Task<IBrowser> Connecting { get; } = connecting;

        // A session per class only, under the gate: the tests that hold it, how often a test joined it, its idle timer
        // and the last result of each test.
        public int Active { get; set; }

        public int Joins { get; set; }

        public ITimer? IdleClose { get; set; }

        public Dictionary<string, TestResultReport> Results { get; } = new(StringComparer.Ordinal);

        public void CancelIdleClose()
        {
            IdleClose?.Dispose();
            IdleClose = null;
        }
    }
}
