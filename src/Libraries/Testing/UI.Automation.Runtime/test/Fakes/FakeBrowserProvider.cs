using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

// A provider without Playwright: enough for the session's start and stop. A test acquires Browser (e.g. a mock) when it
// is set and fails to otherwise. It records the tests that acquired and released a browser and the tests the session
// reported as completed.
internal sealed class FakeBrowserProvider(DeviceCatalog? devices = null) : IBrowserProvider
{
    private readonly List<TestIdentity> completed = [];
    private readonly List<TestIdentity> acquired = [];
    private readonly List<TestResultReport?> released = [];

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

    public IBrowser? Browser { get; init; }

    public IReadOnlyList<TestIdentity> Acquired
    {
        get
        {
            lock (acquired)
            {
                return [.. acquired];
            }
        }
    }

    // The result each release got, in order.
    public IReadOnlyList<TestResultReport?> Released
    {
        get
        {
            lock (released)
            {
                return [.. released];
            }
        }
    }

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
        if (Browser is null)
        {
            throw new NotSupportedException("The fake provider has no browser.");
        }

        lock (acquired)
        {
            acquired.Add(test);
        }

        return ValueTask.FromResult(new BrowserLease(Browser, null, new Dictionary<string, string>()));
    }

    public ValueTask ReleaseAsync(BrowserLease lease, TestResultReport? result, CancellationToken cancellationToken)
    {
        lock (released)
        {
            released.Add(result);
        }

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
