using System.IO.Abstractions;
using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Artifacts;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Data;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime;

// One run of a UI suite, engine-neutral: the engine adapter starts it once before the first test and stops it after
// the last. It holds the run id (yyyyMMdd-HHmmss-xxxx) and the data seed, the browser provider, the context options,
// the user pool, the cached sign-ins and the reporting dispatcher. Start fails fast: unknown sign-in flow, a pool that
// cannot lease, browsers that do not launch, an unknown device or a reporter that refuses its settings stop the run
// before any test, and before a reporter creates a run; a failure after the reporters started completes their runs
// and disposes them. No static state: the adapter keeps the current session.
public sealed class AutomationSession : IAsyncDisposable
{
    private const string RunIdCharacters = "abcdefghijklmnopqrstuvwxyz0123456789";

    private readonly object stopGate = new();
    private Task<TestRunSummary>? stopping;

    private AutomationSession(
        UiAutomationSettings settings,
        string runId,
        int seed,
        DateTimeOffset startedAt,
        string runDirectory,
        TestRunDescriptor run,
        IBrowserProvider browserProvider,
        ContextOptionsFactory contextOptions,
        ISignInFlow? signInFlow,
        UserPool users,
        ReportingDispatcher reporting,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger logger)
    {
        Settings = settings;
        RunId = runId;
        Seed = seed;
        StartedAt = startedAt;
        RunDirectory = runDirectory;
        Run = run;
        BrowserProvider = browserProvider;
        ContextOptions = contextOptions;
        SignInFlow = signInFlow;
        StorageStates = signInFlow is null ? null : new StorageStateCache(signInFlow, logger);
        Users = users;
        Reporting = reporting;
        FileSystem = fileSystem;
        TimeProvider = timeProvider;
        Logger = logger;
    }

    // The version of the Microsoft.Playwright client, e.g. 1.63.0.
    public static string PlaywrightVersion { get; } = typeof(IPlaywright).Assembly.GetName().Version is { } version
        ? $"{version.Major}.{version.Minor}.{version.Build}"
        : "unknown";

    public UiAutomationSettings Settings { get; }

    public string RunId { get; }

    // The session seed of the generated data (Execution:Seed, or random); logged at start.
    public int Seed { get; }

    public DateTimeOffset StartedAt { get; }

    // <ArtifactsPath>/<RunId>: results.jsonl and the artifacts of every test.
    public string RunDirectory { get; }

    public TestRunDescriptor Run { get; }

    public IBrowserProvider BrowserProvider { get; }

    public ContextOptionsFactory ContextOptions { get; }

    public UserPool Users { get; }

    // Null for Target:SignIn None: contexts start without a signed-in state.
    public ISignInFlow? SignInFlow { get; }

    public StorageStateCache? StorageStates { get; }

    public ReportingDispatcher Reporting { get; }

    public IFileSystem FileSystem { get; }

    public TimeProvider TimeProvider { get; }

    public ILogger Logger { get; }

    // Throws InvalidOperationException in skip mode (no Target:BaseUrl) and UiAutomationSettingsException for settings
    // that cannot run.
    public static async Task<AutomationSession> StartAsync(
        UiAutomationSettings settings,
        AutomationSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException($"{UiAutomationSettings.NotConfiguredReason(null)} The session cannot start; the engine adapter skips the tests instead.");
        }

        options ??= new AutomationSessionOptions();
        var logger = options.Logger ?? NullLogger.Instance;
        var fileSystem = options.FileSystem ?? new FileSystem();
        var timeProvider = options.TimeProvider ?? TimeProvider.System;

        var signInFlow = SettingsValidator.ResolveSignInFlow(settings.Target, options.SignInFlows);
        var users = UserPool.FromSettings(settings.Users, timeProvider);
        var seed = settings.Execution.Seed ?? SeededData.NewSessionSeed();
        var startedAt = timeProvider.GetUtcNow();
        var runId = options.RunId ?? NewRunId(timeProvider.GetLocalNow());
        var runDirectory = fileSystem.Path.Combine(settings.ResolvePath(settings.Execution.ArtifactsPath), runId);
        fileSystem.Directory.CreateDirectory(runDirectory);

        var environment = options.Environment ?? RunEnvironment.Current;
        var values = RunNameValues.For(settings, runId, TimeZoneInfo.ConvertTime(startedAt, timeProvider.LocalTimeZone), environment);
        var providerContext = new BrowserProviderContext(settings, values)
        {
            PlannedTests = options.PlannedTests,
            Logger = logger,
            TimeProvider = timeProvider,
        };

        var createProvider = options.BrowserProviderFactory ?? BrowserProviderFactory.CreateAsync;
        var browserProvider = await createProvider(providerContext, cancellationToken);

        IReadOnlyList<ITestRunReporter> reporters = [];
        ReportingDispatcher? reporting = null;
        try
        {
            var contextOptions = new ContextOptionsFactory(settings, browserProvider.Devices);
            Assertions.SetDefaultExpectTimeout(settings.Execution.ExpectTimeoutMs);

            var run = CreateRun(settings, runId, seed, startedAt, environment, values, browserProvider, contextOptions, options.TestEngine);
            reporters = await ReporterSelection.CreateAsync(settings, options.ReporterProviders, options.Services ?? EmptyServiceProvider.Instance, run, logger);

            var dispatcherOptions = ReportingDispatcherOptions.FromSettings(settings.Reporting);
            dispatcherOptions.ResultsFilePath = fileSystem.Path.Combine(runDirectory, ResultsFile.FileName);
            dispatcherOptions.FileSystem = fileSystem;
            dispatcherOptions.TimeProvider = timeProvider;

            reporting = new ReportingDispatcher(reporters, dispatcherOptions, logger);
            await reporting.StartAsync(run, options.PlannedTests, cancellationToken);

            var session = new AutomationSession(
                settings, runId, seed, startedAt, runDirectory, run, browserProvider, contextOptions, signInFlow, users, reporting, fileSystem, timeProvider, logger);
            session.LogStart();
            return session;
        }
        catch
        {
            await AbandonReportingAsync(reporting, reporters, logger);
            await browserProvider.DisposeAsync();
            throw;
        }
    }

    // Sends Reporting:ReplayFile through the reporters instead of running tests; needs no target and no browser.
    public static async Task<ReplayResult> ReplayAsync(
        UiAutomationSettings settings,
        AutomationSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsReplay)
        {
            throw new InvalidOperationException("Reporting:ReplayFile is not set.");
        }

        options ??= new AutomationSessionOptions();
        var logger = options.Logger ?? NullLogger.Instance;
        var fileSystem = options.FileSystem ?? new FileSystem();
        var path = settings.ResolvePath(settings.Reporting.ReplayFile!);

        ResultsFileContent content;
        try
        {
            content = ResultsFile.Read(fileSystem, path);
        }
        catch (FileNotFoundException exception)
        {
            throw new UiAutomationSettingsException($"Reporting:ReplayFile {path} does not exist.", exception);
        }

        if (content.Run is null)
        {
            throw new UiAutomationSettingsException($"Reporting:ReplayFile {path} has no run line; it is not a results file of this runtime.");
        }

        var reporters = await ReporterSelection.CreateAsync(settings, options.ReporterProviders, options.Services ?? EmptyServiceProvider.Instance, content.Run, logger);
        var dispatcherOptions = ReportingDispatcherOptions.FromSettings(settings.Reporting);
        dispatcherOptions.FileSystem = fileSystem;
        dispatcherOptions.TimeProvider = options.TimeProvider;

        await using var reporting = new ReportingDispatcher(reporters, dispatcherOptions, logger);
        foreach (var warning in content.Warnings)
        {
            reporting.AddWarning(warning);
        }

        logger.LogInformation("Replaying {Count} result(s) of run {RunId} from {Path}.", content.Results.Count, content.Run.RunId, path);
        await reporting.StartAsync(content.Run, content.PlannedTests, cancellationToken);
        foreach (var result in content.Results)
        {
            reporting.Report(result);
        }

        var summary = await reporting.CompleteAsync(cancellationToken: cancellationToken);
        LogRunUrls(logger, reporting);
        return new ReplayResult(summary, reporting.RunUrls, content.Results.Count);
    }

    // yyyyMMdd-HHmmss of now and 4 random lower-case letters or digits.
    public static string NewRunId(DateTimeOffset now, Random? random = null)
    {
        random ??= Random.Shared;
        var suffix = string.Create(4, random, static (span, source) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = RunIdCharacters[source.Next(RunIdCharacters.Length)];
            }
        });

        return $"{now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}-{suffix}";
    }

    // The data of one attempt of a test, from the session seed.
    public SeededData CreateData(TestIdentity test, int attempt = 1)
    {
        ArgumentNullException.ThrowIfNull(test);
        return new SeededData(Seed, test.Id, attempt);
    }

    public ArtifactCollector CreateArtifacts(TestIdentity test, int attempt = 1)
    {
        ArgumentNullException.ThrowIfNull(test);
        return new ArtifactCollector(FileSystem, RunDirectory, test, attempt);
    }

    // A report with the session's properties (provider, engine, device, seed) under the given ones. A failure caused by
    // a missing user or browser becomes Blocked; any other outcome than Passed carries the exception as its failure,
    // except that a skip keeps only its reason (the exception's message), without a type or stack trace.
    public TestResultReport CreateReport(
        TestIdentity test,
        TestOutcome outcome,
        DateTimeOffset startedAt,
        TimeSpan duration,
        int attempt,
        Exception? exception = null,
        string? output = null,
        IReadOnlyList<TestArtifact>? artifacts = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(test);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TestRunProperties.Provider] = Settings.Browser.Provider.ToString(),
            [TestRunProperties.Engine] = ContextOptions.Engine.ToString().ToLowerInvariant(),
            [TestRunProperties.Seed] = Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [TestRunProperties.Attempt] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (ContextOptions.Device is { } device)
        {
            values[TestRunProperties.Device] = device;
        }

        foreach (var (key, value) in properties ?? new Dictionary<string, string>())
        {
            values[key] = value;
        }

        var refined = AutomationOutcomes.Refine(outcome, exception);
        var failure = (refined, exception) switch
        {
            (TestOutcome.Passed, _) or (_, null) => null,
            (TestOutcome.Skipped, _) => new TestFailure(exception.Message, null, null, null, null),
            _ => TestFailure.FromException(exception),
        };

        return new TestResultReport(
            test,
            refined,
            startedAt,
            duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
            attempt,
            failure,
            string.IsNullOrEmpty(output) ? null : output,
            artifacts ?? [],
            values);
    }

    // The final report of a test (one per test and attempt that is not run again). Never throws for a reporter (see
    // ReportingDispatcher.Report) or the browser provider, which learns that the test is done.
    public void Report(TestResultReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        Reporting.Report(report);
        try
        {
            BrowserProvider.TestCompleted(report.Test);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "The browser provider did not take the end of {Test}.", report.Test.DisplayName);
        }
    }

    // Once: drains and completes the reporters (with the settings' warnings first), then closes the browsers.
    public Task<TestRunSummary> StopAsync(CancellationToken cancellationToken = default)
    {
        lock (stopGate)
        {
            return stopping ??= StopCoreAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private static TestRunDescriptor CreateRun(
        UiAutomationSettings settings,
        string runId,
        int seed,
        DateTimeOffset startedAt,
        RunEnvironment environment,
        RunNameValues values,
        IBrowserProvider browserProvider,
        ContextOptionsFactory contextOptions,
        string? testEngine)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TestRunProperties.RunId] = runId,
            [TestRunProperties.Target] = WithoutUserInfo(settings.Target.BaseUrl!),
            [TestRunProperties.Provider] = settings.Browser.Provider.ToString(),
            [TestRunProperties.Engine] = contextOptions.Engine.ToString().ToLowerInvariant(),
            [TestRunProperties.Headless] = settings.Browser.Headless ? "true" : "false",
            [TestRunProperties.Seed] = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [TestRunProperties.Machine] = environment.Machine,
            [TestRunProperties.PlaywrightVersion] = PlaywrightVersion,
        };

        foreach (var (key, value) in browserProvider.Properties)
        {
            properties[key] = value;
        }

        AddIfSet(properties, TestRunProperties.Profile, settings.Profile);
        AddIfSet(properties, TestRunProperties.Device, contextOptions.Device);
        AddIfSet(properties, TestRunProperties.Branch, environment.Branch);
        AddIfSet(properties, TestRunProperties.Sha, environment.Sha);
        AddIfSet(properties, TestRunProperties.BuildNumber, environment.BuildNumber);
        AddIfSet(properties, TestRunProperties.BuildUrl, environment.BuildUrl);
        AddIfSet(properties, TestRunProperties.TestEngine, testEngine);

        var name = RunNameFormatter.Format(settings.Reporting.RunName, values);
        var description = settings.Reporting.RunDescription is { } pattern
            ? RunNameFormatter.Format(pattern, values, collapseSpaces: false)
            : RunDescriptions.Table(properties);

        return new TestRunDescriptor(runId, name, description, startedAt, properties);
    }

    private static void AddIfSet(Dictionary<string, string> properties, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[key] = value;
        }
    }

    // The URL without user:password@, which never belongs in a report.
    private static string WithoutUserInfo(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.ToString()
            : url;
    }

    // After a start that failed: completes the runs of started reporters (the dispatcher disposes them), or disposes
    // reporters that no dispatcher took over. Never throws, so the start's own error is the one that surfaces.
    private static async Task AbandonReportingAsync(ReportingDispatcher? reporting, IReadOnlyList<ITestRunReporter> reporters, ILogger logger)
    {
        if (reporting is null)
        {
            await ReporterSelection.DisposeAsync(reporters);
            return;
        }

        try
        {
            await reporting.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The reporters of the failed start did not complete cleanly.");
        }
    }

    private static void LogRunUrls(ILogger logger, ReportingDispatcher reporting)
    {
        foreach (var (system, url) in reporting.RunUrls)
        {
            logger.LogInformation("{System} run: {Url}", system, url);
        }
    }

    private void LogStart()
    {
        Logger.LogInformation(
            "UI automation run {RunId} '{RunName}': seed {Seed} (Execution:Seed repeats the data), Playwright {PlaywrightVersion}, artifacts in {RunDirectory}.",
            RunId,
            Run.Name,
            Seed,
            PlaywrightVersion,
            RunDirectory);
        Logger.LogInformation(
            "UI automation settings from {Sources}:{NewLine}{Settings}",
            string.Join(", ", Settings.Sources),
            Environment.NewLine,
            Settings.Describe());

        foreach (var warning in Settings.Warnings.Concat(ContextOptions.Warnings))
        {
            Logger.LogWarning("{Warning}", warning);
        }
    }

    private async Task<TestRunSummary> StopCoreAsync(CancellationToken cancellationToken)
    {
        var summary = await Reporting.CompleteAsync([.. Settings.Warnings, .. ContextOptions.Warnings], cancellationToken);

        try
        {
            await BrowserProvider.DisposeAsync();
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "The browser provider did not close cleanly.");
        }

        Logger.LogInformation(
            "UI automation run {RunId}: {Passed} passed, {Failed} failed, {Skipped} skipped, {Blocked} blocked, {Warnings} warning(s).",
            RunId,
            summary.Passed,
            summary.Failed,
            summary.Skipped,
            summary.Blocked,
            summary.Warnings.Count);
        LogRunUrls(Logger, Reporting);
        return summary;
    }
}
