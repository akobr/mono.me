using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// Where a test's browser comes from. One provider per session; AcquireAsync and ReleaseAsync are called by parallel
// tests. The browser context of a test is the scope's business: a provider hands out browsers.
public interface IBrowserProvider : IAsyncDisposable
{
    BrowserProviderKind Kind { get; }

    // The parallel remote sessions the provider allows; null when it has no limit of its own.
    int? MaxParallelSessions { get; }

    // The device descriptors of its Playwright, for Browser:Device.
    DeviceCatalog Devices { get; }

    // What the run descriptor shows of it: provider, engine, channel, headless, browser version (TestRunProperties keys).
    IReadOnlyDictionary<string, string> Properties { get; }

    ValueTask<BrowserLease> AcquireAsync(TestIdentity test, CancellationToken cancellationToken);

    // result: the test's report, known at release, e.g. for the pass/fail status of a remote session.
    ValueTask ReleaseAsync(BrowserLease lease, TestResultReport? result, CancellationToken cancellationToken);
}
