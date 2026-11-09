using System.Collections.Concurrent;
using System.Reflection;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Logging;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// The TUnit side of one run. TUnit runs the static hooks of a referenced library, so a consuming project only declares
// [assembly: UiAutomationSetup<T>] and the receivers it wants. Before discovery the settings load with the
// --test-parameter values, because registration-time code (the Requires* skips, [ConfiguredRetry], the BrowserStack
// limiter) runs before Before(TestSession); the hook also hands Execution:MaxParallelTests to TUnit, whose command line
// and TUNIT_MAX_PARALLEL_TESTS still win, raises TUnit's hook timeout (5 minutes) to the settings' StartBudget when that
// is longer, and has the terminal log show the settings' secrets as ***. Before the first test the AutomationSession
// starts with every selected test as planned (the start ends with the setup's target check, when it has one), and with
// its browser provider started only when one of them is a UiTest (ChooseBrowserProviderStart); after the last it stops
// and the run summary goes to the terminal. A failure to load or to start (a failed target check
// included) is logged once and kept, and fails each UI test at once with its message (UiAutomationUnavailableException,
// never retried), instead of failing TUnit's discovery or every test of the assembly.
public static class UiAutomationSession
{
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<(string Id, int Attempt), byte> Claimed = new();
    private static readonly List<PendingReport> Pending = [];
    private static UiAutomationSettings? settings;
    private static IUiAutomationSetup? setup;
    private static Exception? failure;
    private static AutomationSession? current;
    private static bool closed;

    // Whether the run starts its browser with the session (a selected test is a UiTest); the end receiver's reports of
    // plain tests name the browser only then.
    private static bool browserRun;

    // The settings of the run; throws before discovery and when they did not load.
    public static UiAutomationSettings Settings => settings ?? throw new InvalidOperationException(failure is null
        ? "The UI automation settings are not loaded; they load in the Before(TestDiscovery) hook of 42.Testing.UI.Automation.Runtime.TUnit."
        : $"The UI automation settings did not load: {failure.Message}");

    // Null before discovery and when the settings did not load.
    public static UiAutomationSettings? LoadedSettings => settings;

    // The consumer's setup of [assembly: UiAutomationSetup<T>]; null without one.
    public static IUiAutomationSetup? Setup => setup;

    // The running session; null in skip mode, in a replay, before the first test and when the start failed.
    public static AutomationSession? Current
    {
        get
        {
            lock (Gate)
            {
                return current;
            }
        }
    }

    // Why the settings did not load or the session did not start; null when neither happened.
    public static Exception? Failure => failure;

    // Why every UI test is skipped: no Target:BaseUrl, or a replay of Reporting:ReplayFile; null when they run.
    public static string? SkipReason => settings switch
    {
        null => null,
        { IsReplay: true } => $"Reporting:ReplayFile is set: the run sends {settings.Reporting.ReplayFile} to the reporters instead of running tests.",
        { IsConfigured: false } => UiAutomationSettings.NotConfiguredReason(setup?.ReadmeHint),
        _ => null,
    };

    internal static ILogger Logger => ConsoleSessionLogger.Instance;

    // "TUnit 1.78.0", for the run description.
    private static string EngineVersion { get; } = "TUnit " + (typeof(TestContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(TestContext).Assembly.GetName().Version?.ToString()
        ?? "unknown");

    [Before(HookType.TestDiscovery)]
    public static void ConfigureDiscovery(BeforeTestDiscoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // An IDE runs several sessions in one process: each starts from scratch.
        Reset();

        try
        {
            setup = FindSetup();
            settings = UiAutomationSettingsLoader.Load(new UiAutomationSettingsOptions
            {
                UserSecretsAssembly = setup?.GetType().Assembly ?? Assembly.GetEntryAssembly(),
                Parameters = TestParameters.Read(),
                Setup = setup,
            });
        }
        catch (Exception exception)
        {
            failure = exception;
            Logger.LogError("{Message}{NewLine}Every UI test fails until the settings are fixed.", exception.Message, Environment.NewLine);
            return;
        }

        ConsoleSessionLogger.Instance.Redact = SettingsRedaction.CreateRedactor(settings);
        if (SkipReason is { } reason)
        {
            Logger.LogInformation("{Reason} Every UI test is skipped.", reason);
            return;
        }

        context.Settings.Parallelism.MaximumParallelTests = settings.Execution.MaxParallelTests;

        // The start of a test (the lease, a BrowserStack connection, the first sign-in) and the session's start (the
        // target check) run in hooks; under TUnit's default hook timeout a slow start would end cancelled instead of
        // with the pool's or the provider's message.
        if (settings.StartBudget > context.Settings.Timeouts.DefaultHookTimeout)
        {
            context.Settings.Timeouts.DefaultHookTimeout = settings.StartBudget;
        }
    }

    [Before(HookType.TestSession)]
    public static async Task StartAsync(TestSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (settings is null || failure is not null)
        {
            return;
        }

        if (settings.IsReplay)
        {
            await ReplayAsync(settings, context.SessionCancellationToken);
            return;
        }

        if (!settings.IsConfigured)
        {
            return;
        }

        var options = CreateOptions();
        options.PlannedTests = context.AllTests.Select(TestIdentityFactory.Create).ToArray();
        options.BrowserProviderStart = ChooseBrowserProviderStart(context.AllTests.Select(test => test.Metadata.TestDetails.ClassType));
        browserRun = options.BrowserProviderStart == BrowserProviderStart.WithSession;
        options.TestEngine = EngineVersion;

        AutomationSession session;
        try
        {
            session = await AutomationSession.StartAsync(settings, options, context.SessionCancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !context.SessionCancellationToken.IsCancellationRequested)
        {
            // Only the end of the test session itself is a cancellation; any other one (a timeout inside the start) is
            // a start that failed.
            failure = exception;
            lock (Gate)
            {
                Pending.Clear();
            }

            Logger.LogError("The UI automation session did not start: {Message}{NewLine}Every UI test fails.", exception.Message, Environment.NewLine);
            return;
        }

        PendingReport[] waiting;
        lock (Gate)
        {
            current = session;
            waiting = [.. Pending];
            Pending.Clear();
        }

        foreach (var report in waiting)
        {
            Send(session, report);
        }
    }

    [After(HookType.TestSession)]
    public static async Task StopAsync(TestSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        AutomationSession? session;
        lock (Gate)
        {
            session = current;
            current = null;
            closed = true;
            Pending.Clear();
        }

        if (session is null)
        {
            return;
        }

        // The session logs the totals and the run URL of every reporter.
        await session.StopAsync(CancellationToken.None);

        if (session.Reporting.ResultsFilePath is { } results && session.FileSystem.File.Exists(results))
        {
            Logger.LogInformation("Results of the run: {Path}", results);
            context.AddArtifact(new Artifact
            {
                File = new FileInfo(results),
                DisplayName = "results.jsonl",
                Description = $"UI automation run {session.RunId}: every reported result, replayable with Reporting:ReplayFile",
            });
        }

        if (session.Reporting.FailsSession)
        {
            var names = string.Join(", ", session.Reporting.UnmappedTests.Select(test => test.DisplayName));
            throw new InvalidOperationException($"Reporting:UnmappedTests is Fail and {session.Reporting.UnmappedTests.Count} test(s) have no [TestCase]: {names}.");
        }
    }

    // For the Before(Test) hook of a UI test: the session, a skip (no target, a replay) or the failure to load or start.
    internal static AutomationSession Require()
    {
        if (failure is not null)
        {
            var redact = settings is null ? null : SettingsRedaction.CreateRedactor(settings, includeEmails: true);
            var text = failure.ToString();
            var clean = redact is null || redact(text) == text;
            throw new UiAutomationUnavailableException($"UI automation is not available: {redact?.Invoke(failure.Message) ?? failure.Message}", clean ? failure : null);
        }

        if (settings is null)
        {
            throw new InvalidOperationException(
                "The UI automation settings were not loaded: the Before(TestDiscovery) hook of 42.Testing.UI.Automation.Runtime.TUnit did not run.");
        }

        if (SkipReason is { } reason)
        {
            Skip.Test(reason);
        }

        return Current ?? throw new InvalidOperationException("The UI automation session has not started (Before(TestSession) did not run).");
    }

    // One report per test and attempt: the first caller (an AutomationTest's After(Test), which claims every attempt it
    // ends, then the end receiver) wins.
    internal static bool TryClaim(TestIdentity test, int attempt)
    {
        return Claimed.TryAdd((test.Id, attempt), 0);
    }

    // The end receiver's report of a test that did not report itself: a test that does not derive from AutomationTest, or
    // one that an attribute skipped (it runs no hooks). Results before the session started wait for it; a retried
    // attempt and a run without a session (skip mode, a failed start) report nothing.
    internal static async ValueTask ReportFromEngineAsync(TestContext context)
    {
        if (settings is not { IsConfigured: true, IsReplay: false } || failure is not null || await RetryPolicy.WillRetryAsync(context))
        {
            return;
        }

        var test = TestIdentityFactory.Create(context);
        var attempt = context.Execution.CurrentRetryAttempt + 1;
        if (!TryClaim(test, attempt))
        {
            return;
        }

        var result = context.Execution.Result;
        var exception = TestOutcomeMapper.Unwrap(result?.Exception);
        var outcome = TestOutcomeMapper.Map(result?.State ?? TestState.Failed, exception);
        if (outcome == TestOutcome.Skipped)
        {
            exception = SkipReasonOf(context, exception);
        }

        var startedAt = result?.Start ?? context.Execution.TestStart ?? DateTimeOffset.UtcNow;
        var duration = result?.Duration ?? (result is { Start: { } start, End: { } end } ? end - start : TimeSpan.Zero);
        var pending = new PendingReport(test, outcome, startedAt, duration, attempt, exception, CapturedOutput(context), ReportsBrowser(context.Metadata.TestDetails.ClassType));

        AutomationSession? session;
        lock (Gate)
        {
            if (closed)
            {
                return;
            }

            session = current;
            if (session is null)
            {
                Pending.Add(pending);
                return;
            }
        }

        Send(session, pending);
    }

    // With the session when a planned test opens its page before it runs (a UiTest), so browsers that do not launch fail
    // the session before any test; otherwise (only AutomationTests without a browser, plain tests) at the first browser a
    // test or the target check asks for, so such a run never starts Playwright unless one does.
    internal static BrowserProviderStart ChooseBrowserProviderStart(IEnumerable<Type> testClasses)
    {
        ArgumentNullException.ThrowIfNull(testClasses);
        return testClasses.Any(type => typeof(UiTest).IsAssignableFrom(type)) ? BrowserProviderStart.WithSession : BrowserProviderStart.OnFirstLease;
    }

    // Whether the end receiver's report of a test of testClass names the browser (provider, engine and device): true for
    // a UiTest, which opens its page before the test; false for any other AutomationTest, because the receiver reports
    // only one whose scope never began, which opened no browser; null for a plain test, whose report names the browser
    // when the run starts it with the session (a run with a UiTest), as before AutomationTest existed.
    internal static bool? ReportsBrowser(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);

        if (typeof(UiTest).IsAssignableFrom(testClass))
        {
            return true;
        }

        return typeof(AutomationTest).IsAssignableFrom(testClass) ? false : null;
    }

    // The skip reason as the exception of a skipped report, whose message is all the report keeps.
    internal static Exception SkipReasonOf(TestContext context, Exception? exception)
    {
        return new SkipTestException(context.Execution.SkipReason ?? (exception as SkipTestException)?.Reason ?? exception?.Message ?? "Skipped.");
    }

    // The test's standard output, then its error output.
    internal static string? CapturedOutput(TestContext context)
    {
        var output = context.Output.GetStandardOutput();
        var errors = context.Output.GetErrorOutput();
        if (string.IsNullOrEmpty(errors))
        {
            return string.IsNullOrEmpty(output) ? null : output;
        }

        return string.IsNullOrEmpty(output) ? errors : $"{output}{Environment.NewLine}{errors}";
    }

    private static AutomationSessionOptions CreateOptions()
    {
        var options = setup is null ? new AutomationSessionOptions() : AutomationSessionOptions.FromSetup(setup);
        options.Logger = Logger;
        return options;
    }

    private static async Task ReplayAsync(UiAutomationSettings replay, CancellationToken cancellationToken)
    {
        try
        {
            var result = await AutomationSession.ReplayAsync(replay, CreateOptions(), cancellationToken);
            var summary = result.Summary;
            Logger.LogInformation(
                "Replayed {Results} result(s): {Passed} passed, {Failed} failed, {Skipped} skipped, {Blocked} blocked, {Warnings} warning(s).",
                result.Results,
                summary.Passed,
                summary.Failed,
                summary.Skipped,
                summary.Blocked,
                summary.Warnings.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = exception;
            Logger.LogError("The replay of Reporting:ReplayFile failed: {Message}", exception.Message);
        }
    }

    private static void Send(AutomationSession session, PendingReport pending)
    {
        try
        {
            session.Report(session.CreateReport(
                pending.Test,
                pending.Outcome,
                pending.StartedAt,
                pending.Duration,
                pending.Attempt,
                pending.Exception,
                pending.Output,
                browser: pending.Browser ?? browserRun));
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "The result of {Test} was not reported.", pending.Test.DisplayName);
        }
    }

    private static IUiAutomationSetup? FindSetup()
    {
        var attribute = Assembly.GetEntryAssembly()?.GetCustomAttribute<UiAutomationSetupAttribute>();
        if (attribute is null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic))
            {
                try
                {
                    attribute = assembly.GetCustomAttribute<UiAutomationSetupAttribute>();
                }
                catch (Exception exception) when (exception is TypeLoadException or FileNotFoundException or CustomAttributeFormatException)
                {
                    // An assembly whose attributes do not load has no setup.
                }

                if (attribute is not null)
                {
                    break;
                }
            }
        }

        return attribute?.CreateSetup();
    }

    private static void Reset()
    {
        lock (Gate)
        {
            current = null;
            closed = false;
            Pending.Clear();
        }

        Claimed.Clear();
        browserRun = false;
        settings = null;
        setup = null;
        failure = null;
        ConsoleSessionLogger.Instance.Redact = null;
    }

    private sealed record PendingReport(
        TestIdentity Test,
        TestOutcome Outcome,
        DateTimeOffset StartedAt,
        TimeSpan Duration,
        int Attempt,
        Exception? Exception,
        string? Output,
        bool? Browser);
}
