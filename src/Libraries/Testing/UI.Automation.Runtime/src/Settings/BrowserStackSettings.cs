namespace _42.Testing.UI.Automation.Runtime.Settings;

// The BrowserStack section, used when Browser:Provider is BrowserStack. Username and AccessKey are secrets: user
// secrets or UITESTS_BrowserStack__Username / UITESTS_BrowserStack__AccessKey.
public sealed class BrowserStackSettings
{
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

    public string? Project { get; set; }

    // The build name; the run name when unset.
    public string? Build { get; set; }

    public string Os { get; set; } = "Windows";

    public string OsVersion { get; set; } = "11";

    // chrome, edge, playwright-chromium, playwright-firefox or playwright-webkit.
    public string Browser { get; set; } = "chrome";

    public string BrowserVersion { get; set; } = "latest";

    public string? Resolution { get; set; } = "1920x1080";

    // Expects a running BrowserStack Local (with LocalIdentifier when set); the provider does not start it.
    public bool Local { get; set; }

    public string? LocalIdentifier { get; set; }

    // browserstack.playwrightVersion: 1.latest or a version; the version of the Playwright client when unset.
    public string? PlaywrightVersion { get; set; }

    // One remote session per test, named <Class>.<Method>; otherwise one per test class.
    public bool SessionPerTest { get; set; } = true;

    // browserstack.console: disable, errors, warnings, info or verbose.
    public string Console { get; set; } = "errors";

    public bool NetworkLogs { get; set; }

    public bool Debug { get; set; }

    public bool Video { get; set; } = true;

    // The parallel sessions of the BrowserStack plan; the engine adapter limits the tests to it.
    public int MaxParallelSessions { get; set; } = 5;

    public BrowserEngine? GetEngine()
    {
        return Browsers.TryGetValue(Browser, out var engine) ? engine : null;
    }
}
