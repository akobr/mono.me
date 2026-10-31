using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Extensions.Logging;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// The provider of Browser:Provider, started.
public static class BrowserProviderFactory
{
    public static async Task<IBrowserProvider> CreateAsync(UiAutomationSettings settings, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Browser.Provider switch
        {
            BrowserProviderKind.Local => await LocalBrowserProvider.StartAsync(settings.Browser, logger, cancellationToken),
            BrowserProviderKind.BrowserStack => throw new NotSupportedException(
                "Browser:Provider BrowserStack is not available yet (phase 4 of the UI automation libraries); use Browser:Provider Local."),
            _ => throw new NotSupportedException($"Browser:Provider {settings.Browser.Provider} is not supported."),
        };
    }
}
