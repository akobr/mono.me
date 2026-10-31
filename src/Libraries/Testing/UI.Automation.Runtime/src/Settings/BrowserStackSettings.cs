namespace _42.Testing.UI.Automation.Runtime.Settings;

// The BrowserStack section, used when Browser:Provider is BrowserStack. Username and AccessKey are secrets: user
// secrets or UITESTS_BrowserStack__Username / UITESTS_BrowserStack__AccessKey.
public sealed class BrowserStackSettings
{
    public const string DefaultEndpoint = "wss://cdp.browserstack.com/playwright";

    // The values of Console.
    public static readonly IReadOnlyList<string> ConsoleLevels = ["disable", "errors", "warnings", "info", "verbose"];

    // The values of Browser and the engine each of them runs on.
    public static readonly IReadOnlyDictionary<string, BrowserEngine> Browsers = new Dictionary<string, BrowserEngine>(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = BrowserEngine.Chromium,
        ["edge"] = BrowserEngine.Chromium,
        ["playwright-chromium"] = BrowserEngine.Chromium,
        ["playwright-firefox"] = BrowserEngine.Firefox,
        ["playwright-webkit"] = BrowserEngine.Webkit,
    };

    public string? Username { get; set; }

    public string? AccessKey { get; set; }

    // The Playwright endpoint of the grid; the capabilities go into its caps query parameter. Another ws:// or wss://
    // endpoint (a local 'npx playwright run-server') ignores them.
    public string Endpoint { get; set; } = DefaultEndpoint;

    public string? Project { get; set; }

    // The build the sessions are grouped by, with the placeholders of Reporting:RunName; unset: "{Branch}
    // #{BuildNumber}" in a GitHub Actions job, "local {Machine} {Date}" elsewhere.
    public string? Build { get; set; }

    // buildTag: a tag of the build, e.g. nightly.
    public string? BuildTag { get; set; }

    public string Os { get; set; } = "Windows";

    // 10 or 11 for Windows; a macOS name such as Tahoe or Sequoia for OS X.
    public string OsVersion { get; set; } = "11";

    // chrome, edge, playwright-chromium, playwright-firefox or playwright-webkit.
    public string Browser { get; set; } = "chrome";

    // chrome and edge only: latest, latest-1, latest-beta or a version; BrowserStack ignores it for playwright-*.
    public string? BrowserVersion { get; set; } = "latest";

    public string? Resolution { get; set; } = "1920x1080";

    // Expects a running BrowserStack Local (with LocalIdentifier when set); the provider does not start it.
    public bool Local { get; set; }

    public string? LocalIdentifier { get; set; }

    // browserstack.playwrightVersion: 1.latest or a version. Unset, it is not sent and BrowserStack picks the version
    // of the Playwright client (client.playwrightVersion, always sent).
    public string? PlaywrightVersion { get; set; }

    // One remote session per test, named <Class>.<Method>; otherwise one per test class, named <Class>.
    public bool SessionPerTest { get; set; } = true;

    // browserstack.console: disable, errors, warnings, info or verbose.
    public string Console { get; set; } = "errors";

    public bool NetworkLogs { get; set; }

    public bool Debug { get; set; }

    public bool Video { get; set; } = true;

    // The parallel sessions of the BrowserStack plan; the engine adapter limits the tests to it.
    public int MaxParallelSessions { get; set; } = 5;

    // How long a connection may take, a session queued at BrowserStack included.
    public int ConnectTimeoutSeconds { get; set; } = 300;

    // More capabilities as strings, over the ones above, e.g. "browserstack.idleTimeout": "300",
    // "browserstack.maskCommands": "setValues, setCookies" or "browserstack.geoLocation": "DE". The credentials are
    // not set here.
    public Dictionary<string, string> AdditionalCapabilities { get; set; } = new(StringComparer.Ordinal);

    public BrowserEngine? GetEngine()
    {
        return Browsers.TryGetValue(Browser, out var engine) ? engine : null;
    }

    // Browser as BrowserStack takes it: trimmed, in lower case.
    public string GetBrowserName()
    {
        return Browser.Trim().ToLowerInvariant();
    }

    // Whether BrowserVersion applies: chrome and edge; the playwright-* builds have the version of their Playwright.
    public bool UsesBrowserVersion()
    {
        return GetBrowserName() is "chrome" or "edge";
    }

    // The trimmed BrowserVersion of chrome and edge; null for the other browsers and when it is not set.
    public string? GetRequestedBrowserVersion()
    {
        return UsesBrowserVersion() && !string.IsNullOrWhiteSpace(BrowserVersion) ? BrowserVersion.Trim() : null;
    }
}
