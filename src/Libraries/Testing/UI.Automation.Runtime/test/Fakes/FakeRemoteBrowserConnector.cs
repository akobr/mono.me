using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

// A grid without a network: every connection is a FakeRemoteBrowser, recorded with what the provider asked for.
// Fail throws for a connection instead; Hold keeps the connections waiting until it completes.
internal sealed class FakeRemoteBrowserConnector : IRemoteBrowserConnector
{
    private readonly object gate = new();
    private readonly List<Connection> connections = [];

    public DeviceCatalog Devices { get; } = DeviceCatalog.Empty;

    public Func<string, Exception?>? Fail { get; set; }

    public TaskCompletionSource? Hold { get; set; }

    public bool Disposed { get; private set; }

    public IReadOnlyList<Connection> Connections
    {
        get
        {
            lock (gate)
            {
                return [.. connections];
            }
        }
    }

    public async Task<IBrowser> ConnectAsync(BrowserEngine engine, string endpoint, BrowserTypeConnectOptions options, CancellationToken cancellationToken)
    {
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        if (Fail?.Invoke(endpoint) is { } exception)
        {
            throw exception;
        }

        var browser = new FakeRemoteBrowser();
        lock (gate)
        {
            connections.Add(new Connection(engine, endpoint, options, browser));
        }

        return browser.Object;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    internal sealed record Connection(BrowserEngine Engine, string Endpoint, BrowserTypeConnectOptions Options, FakeRemoteBrowser Browser);
}
