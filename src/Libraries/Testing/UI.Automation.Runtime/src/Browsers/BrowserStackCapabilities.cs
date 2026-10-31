using System.Text;
using System.Text.Json;
using _42.Testing.UI.Automation.Runtime.Reporting;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// The capabilities of a BrowserStack session and the endpoint that carries them: a JSON object of strings (as
// BrowserStack's C# sample sends them, "true" and "false" included), URL-encoded into the caps parameter of
// BrowserStack:Endpoint. Names, builds and projects are cut at 255 characters.
public static class BrowserStackCapabilities
{
    public const string UsernameKey = "browserstack.username";
    public const string AccessKeyKey = "browserstack.accessKey";
    public const int MaxNameLength = 255;

    // The default BrowserStack:Build of a GitHub Actions job (GITHUB_RUN_NUMBER is set) and of any other run.
    public const string CiBuild = "{Branch} #{BuildNumber}";
    public const string LocalBuild = "local {Machine} {Date}";

    // browserstack.idleTimeout, and BrowserStack's idle timeout when it is not set.
    public const string IdleTimeoutKey = "browserstack.idleTimeout";
    public const int DefaultIdleTimeoutSeconds = 90;

    // Whether a key of BrowserStack:AdditionalCapabilities would set the credentials.
    public static bool IsCredential(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var name = key.Trim();
        return string.Equals(name, UsernameKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, AccessKeyKey, StringComparison.OrdinalIgnoreCase);
    }

    // In this order: the platform, the dashboard's grouping, the credentials, BrowserStack Local, the Playwright
    // versions, the logs and what they mask; then BrowserStack:AdditionalCapabilities, each replacing a capability of
    // its name.
    // clientPlaywrightVersion: the version of the Microsoft.Playwright client, e.g. 1.63.0.
    public static IReadOnlyList<KeyValuePair<string, string>> Create(BrowserStackSettings settings, string sessionName, string build, string clientPlaywrightVersion)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientPlaywrightVersion);

        var capabilities = new List<KeyValuePair<string, string>>();

        Add(capabilities, "browser", settings.GetBrowserName());
        Add(capabilities, "browser_version", settings.GetRequestedBrowserVersion());

        Add(capabilities, "os", settings.Os);
        Add(capabilities, "os_version", settings.OsVersion);
        Add(capabilities, "resolution", settings.Resolution);
        Add(capabilities, "project", Cut(settings.Project));
        Add(capabilities, "build", Cut(build));
        Add(capabilities, "buildTag", settings.BuildTag);
        Add(capabilities, "name", Cut(sessionName));
        Add(capabilities, UsernameKey, settings.Username);
        Add(capabilities, AccessKeyKey, settings.AccessKey);
        Add(capabilities, "browserstack.local", Flag(settings.Local));
        Add(capabilities, "browserstack.localIdentifier", settings.LocalIdentifier);
        Add(capabilities, "browserstack.playwrightVersion", settings.PlaywrightVersion);
        Add(capabilities, "client.playwrightVersion", clientPlaywrightVersion);
        Add(capabilities, "browserstack.console", settings.Console.Trim().ToLowerInvariant());
        Add(capabilities, "browserstack.networkLogs", Flag(settings.NetworkLogs));
        Add(capabilities, "browserstack.debug", Flag(settings.Debug));
        Add(capabilities, "browserstack.video", Flag(settings.Video));
        Add(capabilities, "browserstack.playwrightLogs", Flag(settings.PlaywrightLogs));
        Add(capabilities, "browserstack.maskCommands", NormalizeList(settings.MaskCommands));

        foreach (var (key, value) in settings.AdditionalCapabilities)
        {
            if (string.IsNullOrWhiteSpace(key) || IsCredential(key))
            {
                continue;
            }

            var name = key.Trim();
            var index = capabilities.FindIndex(capability => string.Equals(capability.Key, name, StringComparison.Ordinal));
            var capability = new KeyValuePair<string, string>(name, value ?? string.Empty);
            if (index >= 0)
            {
                capabilities[index] = capability;
            }
            else
            {
                capabilities.Add(capability);
            }
        }

        return capabilities;
    }

    public static string ToJson(IEnumerable<KeyValuePair<string, string>> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in capabilities)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // endpoint?caps=<the URL-encoded JSON>, or &caps= after a query of its own.
    public static string CreateEndpoint(string endpoint, string capabilitiesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(capabilitiesJson);

        var separator = !endpoint.Contains('?', StringComparison.Ordinal) ? "?"
            : endpoint.EndsWith('?') || endpoint.EndsWith('&') ? string.Empty
            : "&";
        return $"{endpoint}{separator}caps={Uri.EscapeDataString(capabilitiesJson)}";
    }

    // The endpoint without its query, for messages: a caps query holds the access key.
    public static string Describe(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var query = endpoint.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? endpoint : endpoint[..query];
    }

    // How long BrowserStack keeps a session without a command: browserstack.idleTimeout of
    // BrowserStack:AdditionalCapabilities in seconds, or DefaultIdleTimeoutSeconds; at least two seconds.
    public static TimeSpan IdleTimeout(BrowserStackSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var value = settings.AdditionalCapabilities.FirstOrDefault(pair => string.Equals(pair.Key?.Trim(), IdleTimeoutKey, StringComparison.Ordinal)).Value;
        var seconds = int.TryParse(value?.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : DefaultIdleTimeoutSeconds;
        return TimeSpan.FromSeconds(Math.Max(seconds, 2));
    }

    // BrowserStack:Build with the run's placeholders; when unset, CiBuild in a GitHub Actions job and LocalBuild
    // elsewhere.
    public static string ResolveBuild(BrowserStackSettings settings, RunNameValues run)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(run);

        var pattern = !string.IsNullOrWhiteSpace(settings.Build) ? settings.Build : run.BuildNumber is null ? LocalBuild : CiBuild;
        var build = RunNameFormatter.Format(pattern, run);
        return Cut(string.IsNullOrWhiteSpace(build) ? RunNameFormatter.Format(LocalBuild, run) : build)!;
    }

    // <Class>.<Method> of a session per test (the class without its namespace, and the display name, which tells the
    // cases of a data-driven test apart), <Class> of a session per class.
    public static string SessionName(TestIdentity test, bool perTest)
    {
        ArgumentNullException.ThrowIfNull(test);

        var lastDot = test.ClassName.LastIndexOfAny(['.', '+']);
        var className = lastDot >= 0 ? test.ClassName[(lastDot + 1)..] : test.ClassName;
        return Cut(perTest ? $"{className}.{test.DisplayName}" : className)!;
    }

    private static void Add(List<KeyValuePair<string, string>> capabilities, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            capabilities.Add(new(key, value.Trim()));
        }
    }

    // "a, b": the entries trimmed, empty ones dropped; null for none.
    private static string? NormalizeList(string? value)
    {
        var entries = (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries.Length == 0 ? null : string.Join(", ", entries);
    }

    private static string Flag(bool value)
    {
        return value ? "true" : "false";
    }

    private static string? Cut(string? value)
    {
        return value is { Length: > MaxNameLength } ? value[..MaxNameLength] : value;
    }
}
