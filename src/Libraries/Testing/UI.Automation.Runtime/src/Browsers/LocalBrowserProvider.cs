using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// The browsers of the installed Playwright: one IPlaywright and one launched browser per session, shared by every test
// (each test gets its own context). Launched at start, so missing browsers fail the session at once; launched again
// when the browser crashed.
public sealed class LocalBrowserProvider : IBrowserProvider
{
    private readonly IPlaywright playwright;
    private readonly BrowserSettings settings;
    private readonly ILogger logger;
    private readonly SemaphoreSlim launchGate = new(1, 1);
    private volatile IReadOnlyDictionary<string, string> properties;
    private IBrowser? browser;
    private bool disposed;

    private LocalBrowserProvider(IPlaywright playwright, BrowserSettings settings, ILogger logger)
    {
        this.playwright = playwright;
        this.settings = settings;
        this.logger = logger;
        Devices = DeviceCatalog.From(playwright);
        var values = new Dictionary<string, string>
        {
            [TestRunProperties.Provider] = BrowserProviderKind.Local.ToString(),
            [TestRunProperties.Engine] = settings.Engine.ToString().ToLowerInvariant(),
            [TestRunProperties.Headless] = settings.Headless ? "true" : "false",
        };

        if (!string.IsNullOrWhiteSpace(settings.Channel))
        {
            values[TestRunProperties.Channel] = settings.Channel;
        }

        properties = values;
    }

    public BrowserProviderKind Kind => BrowserProviderKind.Local;

    public int? MaxParallelSessions => null;

    public DeviceCatalog Devices { get; }

    public IReadOnlyDictionary<string, string> Properties => properties;

    public static async Task<LocalBrowserProvider> StartAsync(BrowserSettings settings, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IPlaywright playwright;
        try
        {
            playwright = await Playwright.CreateAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new BrowserUnavailableException($"The Playwright driver did not start: {exception.Message}", exception);
        }

        var provider = new LocalBrowserProvider(playwright, settings, logger ?? NullLogger.Instance);
        try
        {
            await provider.GetBrowserAsync(cancellationToken);
            return provider;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<BrowserLease> AcquireAsync(TestIdentity test, CancellationToken cancellationToken)
    {
        var current = await GetBrowserAsync(cancellationToken);
        return new BrowserLease(current, null, properties);
    }

    // The browser stays for the next test; the scope closes the test's context.
    public ValueTask ReleaseAsync(BrowserLease lease, TestResultReport? result, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            if (browser is not null)
            {
                await browser.CloseAsync();
            }
        }
        catch (PlaywrightException exception)
        {
            logger.LogWarning(exception, "The {Engine} browser did not close cleanly.", settings.Engine);
        }

        playwright.Dispose();
        launchGate.Dispose();
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (browser is { IsConnected: true } connected)
        {
            return connected;
        }

        await launchGate.WaitAsync(cancellationToken);
        try
        {
            if (browser is { IsConnected: true } launched)
            {
                return launched;
            }

            if (browser is not null)
            {
                logger.LogWarning("The {Engine} browser was disconnected; launching it again.", settings.Engine);
            }

            browser = await LaunchAsync();

            // A new dictionary, so leases handed out before keep reading theirs.
            properties = new Dictionary<string, string>(properties)
            {
                [TestRunProperties.BrowserVersion] = browser.Version,
            };

            return browser;
        }
        finally
        {
            launchGate.Release();
        }
    }

    private async Task<IBrowser> LaunchAsync()
    {
        var type = settings.Engine switch
        {
            BrowserEngine.Firefox => playwright.Firefox,
            BrowserEngine.Webkit => playwright.Webkit,
            _ => playwright.Chromium,
        };

        var options = new BrowserTypeLaunchOptions
        {
            Headless = settings.Headless,
            SlowMo = settings.SlowMoMs > 0 ? settings.SlowMoMs : null,
            Channel = string.IsNullOrWhiteSpace(settings.Channel) ? null : settings.Channel,
        };

        try
        {
            return await type.LaunchAsync(options);
        }
        catch (PlaywrightException exception)
        {
            // A channel (chrome, msedge, …) is a branded browser of its own, installed by its name.
            var browser = options.Channel ?? settings.Engine.ToString().ToLowerInvariant();
            throw new BrowserUnavailableException(
                $"The {browser} browser did not launch; install it with 'pwsh bin/<configuration>/net10.0/playwright.ps1 install {browser}' in the test project. {exception.Message}",
                exception);
        }
    }
}
