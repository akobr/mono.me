using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

// A provider without Playwright: enough for the session's start and stop; no test acquires a browser from it. It
// records the tests the session reported as completed.
internal sealed class FakeBrowserProvider(DeviceCatalog? devices = null) : IBrowserProvider
{
    private readonly List<TestIdentity> completed = [];

    public BrowserProviderKind Kind => BrowserProviderKind.Local;

    public int? MaxParallelSessions => null;

    public DeviceCatalog Devices { get; } = devices ?? DeviceCatalog.Empty;

    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>
    {
        [TestRunProperties.Provider] = "Local",
        [TestRunProperties.Engine] = "chromium",
        [TestRunProperties.BrowserVersion] = "142.0",
    };

    public bool Disposed { get; private set; }

    public Exception? FailCompleted { get; set; }

    public IReadOnlyList<TestIdentity> Completed
    {
        get
        {
            lock (completed)
            {
                return [.. completed];
            }
        }
    }

    public ValueTask<BrowserLease> AcquireAsync(TestIdentity test, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The fake provider has no browser.");
    }

    public ValueTask ReleaseAsync(BrowserLease lease, TestResultReport? result, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public void TestCompleted(TestIdentity test)
    {
        lock (completed)
        {
            completed.Add(test);
        }

        if (FailCompleted is { } failure)
        {
            throw failure;
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
