using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Logging;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// The session's provider with BrowserProviderStart.OnFirstLease: it starts the real one at the first AcquireAsync, once,
// also when several tests ask at the same time. Until then it knows only the settings: the provider's kind and session
// limit, no device descriptors and no properties. The start runs without the caller's token, so a test that gives up
// does not cancel it for the others. A start that failed is not tried again: every acquisition fails with its message
// as a BrowserUnavailableException (a Blocked test). The tests the session reported as completed before the start reach
// the started provider at once, so a BrowserStack session per class knows which of its tests are done. Disposing it
// disposes the started provider, after a start that is still running; nothing when none was asked for.
internal sealed class LazyBrowserProvider : IBrowserProvider
{
    private static readonly IReadOnlyDictionary<string, string> NoProperties = new Dictionary<string, string>();

    private readonly Func<CancellationToken, Task<IBrowserProvider>> start;
    private readonly ILogger logger;
    private readonly int? configuredMaxParallelSessions;
    private readonly object gate = new();
    private readonly List<TestIdentity> completedBeforeStart = [];
    private Task<IBrowserProvider>? starting;
    private IBrowserProvider? started;
    private bool disposed;

    public LazyBrowserProvider(UiAutomationSettings settings, Func<CancellationToken, Task<IBrowserProvider>> start, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(logger);

        this.start = start;
        this.logger = logger;
        Kind = settings.Browser.Provider;
        configuredMaxParallelSessions = Kind == BrowserProviderKind.BrowserStack ? settings.BrowserStack.MaxParallelSessions : null;
    }

    public BrowserProviderKind Kind { get; }

    public int? MaxParallelSessions => Started?.MaxParallelSessions ?? configuredMaxParallelSessions;

    public DeviceCatalog Devices => Started?.Devices ?? DeviceCatalog.Empty;

    public IReadOnlyDictionary<string, string> Properties => Started?.Properties ?? NoProperties;

    // Whether a scope asked for a browser and the real provider started.
    public bool IsStarted => Started is not null;

    private IBrowserProvider? Started
    {
        get
        {
            lock (gate)
            {
                return started;
            }
        }
    }

    public async ValueTask<BrowserLease> AcquireAsync(TestIdentity test, CancellationToken cancellationToken)
    {
        var provider = await StartAsync(cancellationToken);
        return await provider.AcquireAsync(test, cancellationToken);
    }

    // A lease comes only from the started provider.
    public ValueTask ReleaseAsync(BrowserLease lease, TestResultReport? result, CancellationToken cancellationToken)
    {
        return Started is { } provider ? provider.ReleaseAsync(lease, result, cancellationToken) : ValueTask.CompletedTask;
    }

    public void TestCompleted(TestIdentity test)
    {
        ArgumentNullException.ThrowIfNull(test);

        IBrowserProvider? provider;
        lock (gate)
        {
            provider = started;
            if (provider is null)
            {
                if (!disposed)
                {
                    completedBeforeStart.Add(test);
                }

                return;
            }
        }

        provider.TestCompleted(test);
    }

    public async ValueTask DisposeAsync()
    {
        Task<IBrowserProvider>? pending;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pending = starting;
            completedBeforeStart.Clear();
        }

        if (pending is null)
        {
            return;
        }

        IBrowserProvider provider;
        try
        {
            provider = await pending;
        }
        catch (Exception)
        {
            // The tests that asked got the failure of the start; a start that ended after the dispose closed its provider.
            return;
        }

        await provider.DisposeAsync();
    }

    private async Task<IBrowserProvider> StartAsync(CancellationToken cancellationToken)
    {
        Task<IBrowserProvider> current;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            current = starting ??= StartCoreAsync();
        }

        try
        {
            return await current.WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (current.IsCompleted && !current.IsCompletedSuccessfully && exception is not ObjectDisposedException)
        {
            // A new exception per test: the failed start is shared, and its exception would collect the stack of each.
            throw new BrowserUnavailableException(exception.Message, exception);
        }
    }

    private async Task<IBrowserProvider> StartCoreAsync()
    {
        // Out of the gate of StartAsync, which runs this method's first part.
        await Task.Yield();

        logger.LogInformation("Starting the {Provider} browser provider for the first browser the run asked for.", Kind);
        var provider = await start(CancellationToken.None);

        lock (gate)
        {
            if (!disposed)
            {
                started = provider;
                foreach (var test in completedBeforeStart)
                {
                    try
                    {
                        provider.TestCompleted(test);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "The browser provider did not take the end of {Test}.", test.DisplayName);
                    }
                }

                completedBeforeStart.Clear();
                return provider;
            }
        }

        await provider.DisposeAsync();
        throw new ObjectDisposedException(GetType().FullName);
    }
}
