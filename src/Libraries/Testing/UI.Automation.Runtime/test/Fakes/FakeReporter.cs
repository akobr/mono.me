using System.Collections.Concurrent;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

internal sealed class FakeReporter(string system = "Fake") : ITestRunReporter
{
    private int reportCalls;

    public string System { get; } = system;

    public Uri? RunUrl { get; private set; }

    public ConcurrentQueue<IReadOnlyList<TestResultReport>> Batches { get; } = new();

    public TestRunDescriptor? StartedRun { get; private set; }

    public IReadOnlyList<TestIdentity> PlannedTests { get; private set; } = [];

    public TestRunSummary? CompletedSummary { get; private set; }

    public bool Disposed { get; private set; }

    public int ReportCalls => Volatile.Read(ref reportCalls);

    // Thrown by StartAsync when set.
    public Exception? StartFailure { get; init; }

    // Called with the 1-based number of the ReportAsync call; an exception it returns is thrown.
    public Func<int, Exception?>? ReportFailure { get; init; }

    // ReportAsync waits for it (ignoring the token) when set.
    public TaskCompletionSource? ReportGate { get; init; }

    public IEnumerable<TestResultReport> Results => Batches.SelectMany(batch => batch);

    public Task StartAsync(TestRunDescriptor run, IReadOnlyList<TestIdentity> plannedTests, CancellationToken cancellationToken)
    {
        if (StartFailure is not null)
        {
            throw StartFailure;
        }

        StartedRun = run;
        PlannedTests = plannedTests;
        RunUrl = new Uri($"https://reports.example.test/runs/{run.RunId}");
        return Task.CompletedTask;
    }

    public async Task ReportAsync(IReadOnlyList<TestResultReport> results, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref reportCalls);
        if (ReportGate is not null)
        {
            await ReportGate.Task;
        }

        if (ReportFailure?.Invoke(call) is { } failure)
        {
            throw failure;
        }

        Batches.Enqueue(results);
    }

    public Task CompleteAsync(TestRunSummary summary, CancellationToken cancellationToken)
    {
        CompletedSummary = summary;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
