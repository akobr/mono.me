using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

// A provider without Playwright: enough for the session's start and stop; no test acquires a browser from it.
internal sealed class FakeBrowserProvider(DeviceCatalog? devices = null) : IBrowserProvider
{
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

    public ValueTask<BrowserLease> AcquireAsync(TestIdentity test, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The fake provider has no browser.");
    }

    public ValueTask ReleaseAsync(BrowserLease lease, TestResultReport? result, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
