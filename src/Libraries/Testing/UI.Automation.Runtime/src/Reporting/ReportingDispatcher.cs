using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Threading.Channels;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

// Takes the reports of the tests and hands them to the reporters without ever blocking or failing a test. Report
// appends each one to results.jsonl and queues it on one channel per reporter, whose background consumer sends it in
// batches (BatchSize, or what arrived within FlushInterval). Every reporter call is isolated: a transient failure is
// retried with exponential back-off up to MaxAttempts, anything else becomes a run warning and the batch is dropped
// (it stays in results.jsonl). A reporter whose start failed gets nothing more. CompleteAsync drains with a deadline,
// so a stuck reporter cannot hang the process, then completes and disposes the reporters. Zero reporters is valid:
// the results file is still written.
public sealed class ReportingDispatcher : IAsyncDisposable
{
    private const int MaxListedTests = 10;

    private readonly ReportingDispatcherOptions options;
    private readonly ReporterChannel[] channels;
    private readonly ILogger logger;
    private readonly TimeProvider timeProvider;
    private readonly ResultsFile? resultsFile;
    private readonly ConcurrentQueue<string> warnings = new();
    private readonly ConcurrentDictionary<string, TestResultReport> lastReports = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TestIdentity> plannedTests = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource stopping = new();
    private readonly object completionGate = new();
    private IReadOnlyList<string> leadingWarnings = [];
    private IReadOnlyList<TestIdentity> unmappedTests = [];
    private Task<TestRunSummary>? completion;
    private int started;
    private int completing;
    private int resultsFileFailed;
    private int lateReports;

    public ReportingDispatcher(IEnumerable<ITestRunReporter> reporters, ReportingDispatcherOptions? options = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(reporters);

        this.options = options ?? new ReportingDispatcherOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.BatchSize, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaxAttempts, 1, nameof(options));

        channels = reporters.Select(reporter => new ReporterChannel(reporter)).ToArray();
        this.logger = logger ?? NullLogger.Instance;
        timeProvider = this.options.TimeProvider ?? TimeProvider.System;

        if (!string.IsNullOrWhiteSpace(this.options.ResultsFilePath))
        {
            resultsFile = new ResultsFile(this.options.FileSystem ?? new FileSystem(), this.options.ResultsFilePath);
        }
    }

    public IReadOnlyList<ITestRunReporter> Reporters => channels.Select(channel => channel.Reporter).ToArray();

    public string? ResultsFilePath => resultsFile?.Path;

    public IReadOnlyList<string> Warnings => [.. leadingWarnings, .. warnings];

    // The tests without a [TestCase] for one of the reporters; known once CompleteAsync ran.
    public IReadOnlyList<TestIdentity> UnmappedTests => unmappedTests;

    // Reporting:UnmappedTests is Fail and a test has no case: the engine adapter fails the session.
    public bool FailsSession => options.UnmappedTests == UnmappedTestsPolicy.Fail && unmappedTests.Count > 0;

    // The runs the reporters created, by system.
    public IReadOnlyDictionary<string, Uri> RunUrls => channels
        .Where(channel => channel.Reporter.RunUrl is not null)
        .GroupBy(channel => channel.System, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Reporter.RunUrl!, StringComparer.OrdinalIgnoreCase);

    // Writes the run line of results.jsonl, starts every reporter (each bounded by CompleteTimeout) and their consumers.
    public async Task StartAsync(TestRunDescriptor run, IReadOnlyList<TestIdentity> planned, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(planned);

        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("The dispatcher was started already.");
        }

        foreach (var test in planned)
        {
            plannedTests[test.Id] = test;
        }

        WriteResultsFile(file => file.WriteRun(run, planned));

        await Task.WhenAll(channels.Select(channel => StartReporterAsync(channel, run, planned, cancellationToken)));

        foreach (var channel in channels.Where(channel => channel.IsActive))
        {
            channel.Consumer = Task.Run(() => ConsumeAsync(channel), CancellationToken.None);
        }
    }

    // Thread-safe and never throws for a reporter: appends to results.jsonl and queues the report for each reporter.
    // The last report of a test counts in the summary. Called after StartAsync, which starts results.jsonl.
    public void Report(TestResultReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lastReports[report.Test.Id] = report;
        WriteResultsFile(file => file.Append(report));

        foreach (var channel in channels.Where(channel => channel.IsActive))
        {
            if (channel.Queue.Writer.TryWrite(ForSystem(report, channel.System)))
            {
                channel.AddPending(1);
            }
            else if (Volatile.Read(ref completing) != 0 && Interlocked.Increment(ref lateReports) == 1)
            {
                AddWarning($"{channel.System}: a result arrived after the run was completed and was not sent ({report.Test.DisplayName}).");
            }
        }
    }

    public void AddWarning(string warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);

        warnings.Enqueue(warning);
        logger.LogWarning("{Warning}", warning);
    }

    // Once; later calls return the same summary. extraWarnings (e.g. the settings' warnings) come first in the summary.
    public Task<TestRunSummary> CompleteAsync(IEnumerable<string>? extraWarnings = null, CancellationToken cancellationToken = default)
    {
        lock (completionGate)
        {
            return completion ??= CompleteCoreAsync(extraWarnings?.ToArray() ?? [], cancellationToken);
        }
    }

    // Completes the run when CompleteAsync was not called. The stopping source is not disposed: a consumer abandoned at
    // the drain deadline may still hold its token.
    public async ValueTask DisposeAsync()
    {
        await CompleteAsync();
    }

    private static TestResultReport ForSystem(TestResultReport report, string system)
    {
        return report.Test.Cases.All(link => link.AppliesTo(system))
            ? report
            : report with { Test = ForSystem(report.Test, system) };
    }

    private static TestIdentity ForSystem(TestIdentity test, string system)
    {
        return test.Cases.All(link => link.AppliesTo(system))
            ? test
            : test with { Cases = test.Cases.Where(link => link.AppliesTo(system)).ToArray() };
    }

    private static string Name(TestIdentity test)
    {
        var lastDot = test.ClassName.LastIndexOfAny(['.', '+']);
        return $"{(lastDot >= 0 ? test.ClassName[(lastDot + 1)..] : test.ClassName)}.{test.DisplayName}";
    }

    private async Task StartReporterAsync(ReporterChannel channel, TestRunDescriptor run, IReadOnlyList<TestIdentity> planned, CancellationToken cancellationToken)
    {
        var forSystem = planned.Select(test => ForSystem(test, channel.System)).ToArray();
        var deadline = timeProvider.GetUtcNow() + options.CompleteTimeout;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, cancellationToken);
        var startedRun = await CallAsync(channel, "starting the run", token => channel.Reporter.StartAsync(run, forSystem, token), linked, deadline);

        if (!startedRun)
        {
            channel.IsActive = false;
            channel.Queue.Writer.TryComplete();
            AddWarning($"{channel.System}: nothing is reported to it in this run; the results stay in {ResultsFilePath ?? "no results file"}.");
        }
    }

    private async Task ConsumeAsync(ReporterChannel channel)
    {
        var reader = channel.Queue.Reader;
        var token = stopping.Token;
        var batch = new List<TestResultReport>(options.BatchSize);

        try
        {
            while (await reader.WaitToReadAsync(token))
            {
                batch.Clear();
                await FillBatchAsync(reader, batch, token);
                if (batch.Count == 0)
                {
                    continue;
                }

                var results = batch.ToArray();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
                await CallAsync(channel, $"sending {results.Length} result(s)", call => channel.Reporter.ReportAsync(results, call), linked, deadline: null);
                channel.AddPending(-results.Length);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The drain deadline passed; CompleteAsync reports what was left.
        }
        catch (Exception exception)
        {
            AddWarning($"{channel.System}: the consumer stopped: {exception.GetType().Name}: {exception.Message}");
        }
    }

    // Up to BatchSize results: what is queued, then what arrives within FlushInterval, or until the channel completes.
    private async Task FillBatchAsync(ChannelReader<TestResultReport> reader, List<TestResultReport> batch, CancellationToken token)
    {
        using var window = new CancellationTokenSource(options.FlushInterval, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(window.Token, token);

        while (batch.Count < options.BatchSize)
        {
            while (batch.Count < options.BatchSize && reader.TryRead(out var report))
            {
                batch.Add(report);
            }

            if (batch.Count >= options.BatchSize)
            {
                return;
            }

            try
            {
                if (!await reader.WaitToReadAsync(linked.Token))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested && !token.IsCancellationRequested)
            {
                return;
            }
        }
    }

    // True when the call succeeded. A transient failure is retried with back-off; anything else, the last attempt, or
    // the deadline becomes a warning. Throws only when the dispatcher is stopping.
    private async Task<bool> CallAsync(
        ReporterChannel channel,
        string operation,
        Func<CancellationToken, Task> call,
        CancellationTokenSource source,
        DateTimeOffset? deadline)
    {
        var token = source.Token;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var task = call(token);
                if (deadline is { } end)
                {
                    var remaining = end - timeProvider.GetUtcNow();
                    await task.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, timeProvider, CancellationToken.None);
                }
                else
                {
                    await task;
                }

                return true;
            }
            catch (TimeoutException) when (deadline is { } end && timeProvider.GetUtcNow() >= end)
            {
                await source.CancelAsync();
                AddWarning($"{channel.System}: {operation} did not finish within {options.CompleteTimeout.TotalSeconds:0} s.");
                return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested && stopping.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < options.MaxAttempts && TransientFailures.IsTransient(exception, token))
            {
                var delay = Backoff(attempt, exception);
                logger.LogInformation(
                    "{System}: {Operation} failed transiently (attempt {Attempt} of {MaxAttempts}); trying again in {Delay}: {Message}",
                    channel.System,
                    operation,
                    attempt,
                    options.MaxAttempts,
                    delay,
                    exception.Message);

                if (deadline is { } end && timeProvider.GetUtcNow() + delay >= end)
                {
                    AddWarning($"{channel.System}: {operation} failed and the deadline leaves no time to try again: {exception.GetType().Name}: {exception.Message}");
                    return false;
                }

                await Task.Delay(delay, timeProvider, token);
            }
            catch (Exception exception)
            {
                var attempts = attempt > 1 ? $" after {attempt} attempts" : string.Empty;
                AddWarning($"{channel.System}: {operation} failed{attempts}: {exception.GetType().Name}: {exception.Message}");
                return false;
            }
        }
    }

    private TimeSpan Backoff(int attempt, Exception exception)
    {
        var factor = Math.Pow(2, Math.Min(attempt - 1, 30));
        var delay = TimeSpan.FromTicks((long)Math.Min(options.InitialBackoff.Ticks * factor, options.MaxBackoff.Ticks));
        return TransientFailures.RetryAfter(exception) is { } retryAfter && retryAfter > delay ? retryAfter : delay;
    }

    private async Task<TestRunSummary> CompleteCoreAsync(IReadOnlyList<string> extraWarnings, CancellationToken cancellationToken)
    {
        leadingWarnings = extraWarnings;
        Volatile.Write(ref completing, 1);

        foreach (var channel in channels)
        {
            channel.Queue.Writer.TryComplete();
        }

        await DrainAsync(cancellationToken);
        CollectUnmappedTests();

        var summary = BuildSummary();
        var deadline = timeProvider.GetUtcNow() + options.CompleteTimeout;
        await Task.WhenAll(channels.Where(channel => channel.IsActive).Select(async channel =>
        {
            using var source = new CancellationTokenSource();
            await CallAsync(channel, "completing the run", token => channel.Reporter.CompleteAsync(summary, token), source, deadline);
        }));

        foreach (var channel in channels)
        {
            await DisposeReporterAsync(channel);
        }

        if (!stopping.IsCancellationRequested)
        {
            await stopping.CancelAsync();
        }

        return summary with { Warnings = Warnings };
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        var consumers = channels.Select(channel => channel.Consumer).OfType<Task>().ToArray();
        if (consumers.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(consumers).WaitAsync(options.CompleteTimeout, timeProvider, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            await stopping.CancelAsync();
            foreach (var channel in channels.Where(channel => channel.IsActive && channel.Pending > 0))
            {
                var replay = ResultsFilePath is null ? string.Empty : $"; send them again with Reporting:ReplayFile={ResultsFilePath}";
                AddWarning($"{channel.System}: {channel.Pending} result(s) were not sent within {options.CompleteTimeout.TotalSeconds:0} s{replay}.");
            }
        }
    }

    private void CollectUnmappedTests()
    {
        if (options.UnmappedTests == UnmappedTestsPolicy.Ignore || channels.Length == 0)
        {
            return;
        }

        var tests = new Dictionary<string, TestIdentity>(plannedTests, StringComparer.Ordinal);
        foreach (var report in lastReports.Values)
        {
            tests.TryAdd(report.Test.Id, report.Test);
        }

        var unmapped = new Dictionary<string, TestIdentity>(StringComparer.Ordinal);
        foreach (var system in channels.Select(channel => channel.System).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var withoutCase = tests.Values
                .Where(test => !test.Cases.Any(link => link.AppliesTo(system)))
                .OrderBy(test => test.Id, StringComparer.Ordinal)
                .ToArray();

            if (withoutCase.Length == 0)
            {
                continue;
            }

            foreach (var test in withoutCase)
            {
                unmapped.TryAdd(test.Id, test);
            }

            var names = string.Join(", ", withoutCase.Take(MaxListedTests).Select(Name));
            var more = withoutCase.Length > MaxListedTests ? $" and {withoutCase.Length - MaxListedTests} more" : string.Empty;
            AddWarning($"{system}: {withoutCase.Length} test(s) have no [TestCase]: {names}{more}.");
        }

        unmappedTests = unmapped.Values.OrderBy(test => test.Id, StringComparer.Ordinal).ToArray();
    }

    private TestRunSummary BuildSummary()
    {
        int passed = 0, failed = 0, skipped = 0, blocked = 0;
        foreach (var report in lastReports.Values)
        {
            switch (report.Outcome)
            {
                case TestOutcome.Passed:
                    passed++;
                    break;
                case TestOutcome.Skipped:
                    skipped++;
                    break;
                case TestOutcome.Blocked:
                    blocked++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        return new TestRunSummary(timeProvider.GetUtcNow(), passed, failed, skipped, blocked, Warnings);
    }

    private async Task DisposeReporterAsync(ReporterChannel channel)
    {
        try
        {
            await channel.Reporter.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), timeProvider, CancellationToken.None);
        }
        catch (Exception exception)
        {
            AddWarning($"{channel.System}: disposing the reporter failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void WriteResultsFile(Action<ResultsFile> write)
    {
        if (resultsFile is null || Volatile.Read(ref resultsFileFailed) != 0)
        {
            return;
        }

        try
        {
            write(resultsFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (Interlocked.Exchange(ref resultsFileFailed, 1) == 0)
            {
                AddWarning($"{resultsFile.Path} could not be written, so this run cannot be replayed: {exception.Message}");
            }
        }
    }

    private sealed class ReporterChannel(ITestRunReporter reporter)
    {
        private int pending;
        private volatile bool isActive = true;

        public ITestRunReporter Reporter { get; } = reporter;

        public string System { get; } = reporter.System;

        public Channel<TestResultReport> Queue { get; } = Channel.CreateUnbounded<TestResultReport>(new UnboundedChannelOptions { SingleReader = true });

        // False once its start failed.
        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }

        public Task? Consumer { get; set; }

        // Results queued and not yet sent or dropped.
        public int Pending => Volatile.Read(ref pending);

        public void AddPending(int count)
        {
            Interlocked.Add(ref pending, count);
        }
    }
}
