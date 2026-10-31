using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Settings;

// The Browser section: which browser runs where, and the context every test gets.
public sealed class BrowserSettings
{
    public BrowserProviderKind Provider { get; set; } = BrowserProviderKind.Local;

    // The engine of the Local provider; BrowserStack derives it from BrowserStack:Browser.
    public BrowserEngine Engine { get; set; } = BrowserEngine.Chromium;

    // A branded Chromium build of the Local provider: chrome, msedge, chrome-beta, …; Chromium only.
    public string? Channel { get; set; }

    public bool Headless { get; set; } = true;

    public int SlowMoMs { get; set; }

    // A Playwright device descriptor, e.g. "Pixel 7", for an emulated mobile; wins over Viewport.
    public string? Device { get; set; }

    public ViewportSettings Viewport { get; set; } = new();

    // Fixed by default so dates and numbers render the same on every machine; null for the browser's own.
    public string? Locale { get; set; } = "en-US";

    public string? TimeZone { get; set; } = "UTC";

    public ColorScheme? ColorScheme { get; set; } = Microsoft.Playwright.ColorScheme.Light;
}
