using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// Connects through the installed Playwright driver, whose BrowserType of the engine speaks to the remote endpoint.
// Playwright's BrowserType.ConnectAsync takes no token, so a caller that gives up stops waiting and the connection is
// closed once it opens: no session is left behind.
internal sealed class PlaywrightBrowserConnector : IRemoteBrowserConnector
{
    private readonly IPlaywright playwright;

    private PlaywrightBrowserConnector(IPlaywright playwright)
    {
        this.playwright = playwright;
        Devices = DeviceCatalog.From(playwright);
    }

    public DeviceCatalog Devices { get; }

    public static async Task<PlaywrightBrowserConnector> StartAsync()
    {
        return new PlaywrightBrowserConnector(await Playwright.CreateAsync());
    }

    public async Task<IBrowser> ConnectAsync(BrowserEngine engine, string endpoint, BrowserTypeConnectOptions options, CancellationToken cancellationToken)
    {
        var type = engine switch
        {
            BrowserEngine.Firefox => playwright.Firefox,
            BrowserEngine.Webkit => playwright.Webkit,
            _ => playwright.Chromium,
        };

        var connecting = type.ConnectAsync(endpoint, options);
        try
        {
            return await connecting.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = CloseLateAsync(connecting);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        playwright.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task CloseLateAsync(Task<IBrowser> connecting)
    {
        try
        {
            var browser = await connecting;
            await browser.CloseAsync();
        }
        catch (Exception)
        {
            // Nobody waits for this connection any more; its failure was not asked for.
        }
    }
}
