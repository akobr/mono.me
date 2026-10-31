using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace _42.Testing.UI.Automation.Runtime.Settings;

// The effective settings of a run, from UiAutomationSettingsLoader. The sections are bound one by one, so a consuming
// project reads its own keys with Bind<T>(section) from the same configuration.
public sealed class UiAutomationSettings
{
    private readonly ConcurrentDictionary<(Type Type, string Section), object> extensions = new();
    private readonly List<string> warnings = [];

    // Settings in code, with every default and an empty configuration; the loader is the usual way.
    public UiAutomationSettings()
        : this(new ConfigurationBuilder().Build(), AppContext.BaseDirectory)
    {
    }

    internal UiAutomationSettings(IConfiguration configuration, string baseDirectory)
    {
        Configuration = configuration;
        BaseDirectory = baseDirectory;
    }

    public TargetSettings Target { get; set; } = new();

    public BrowserSettings Browser { get; set; } = new();

    public ExecutionSettings Execution { get; set; } = new();

    public UsersSettings Users { get; set; } = new();

    public BrowserStackSettings BrowserStack { get; set; } = new();

    public ReportingSettings Reporting { get; set; } = new();

    // Every layer of the configuration, the consumer's keys included.
    public IConfiguration Configuration { get; }

    // Where testsettings*.json were read from; relative paths of the settings are resolved against it.
    public string BaseDirectory { get; }

    // The profile(s) the loader applied, as given (several are separated by commas); null without one.
    public string? Profile { get; init; }

    // The layers that existed, lowest first, e.g. "testsettings.json", "user secrets", "UITESTS_* variables".
    public IReadOnlyList<string> Sources { get; init; } = [];

    // What the loader adjusted or found doubtful, e.g. a parallelism lowered to the pool size.
    public IReadOnlyList<string> Warnings => warnings;

    // False when Target:BaseUrl is empty: the engine adapter skips every test.
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Target.BaseUrl);

    // A results file is to be sent through the reporters instead of running tests.
    public bool IsReplay => !string.IsNullOrWhiteSpace(Reporting.ReplayFile);

    // How long the start of a test, or the target check, may take within the settings' own timeouts: the lease
    // (Users:LeaseTimeoutSeconds), a BrowserStack connection (BrowserStack:ConnectTimeoutSeconds, queueing included),
    // the user's first sign-in and the page (5 × Execution:NavigationTimeoutMs) and a minute more. An engine adapter
    // gives its hooks at least this long, so the pool's and the provider's timeouts end a slow start, with their message.
    public TimeSpan StartBudget => TimeSpan.FromSeconds(Math.Max(Users.LeaseTimeoutSeconds, 0))
        + (Browser.Provider == BrowserProviderKind.BrowserStack ? TimeSpan.FromSeconds(Math.Max(BrowserStack.ConnectTimeoutSeconds, 0)) : TimeSpan.Zero)
        + TimeSpan.FromMilliseconds(5.0 * Math.Max(Execution.NavigationTimeoutMs, 0))
        + TimeSpan.FromMinutes(1);

    // The engine the browsers run: Browser:Engine, or the one BrowserStack:Browser runs on.
    public BrowserEngine EffectiveEngine => Browser.Provider == BrowserProviderKind.BrowserStack
        ? BrowserStack.GetEngine() ?? Browser.Engine
        : Browser.Engine;

    // The skip reason of a suite without a target. readmeHint: where the consuming project documents its settings.
    public static string NotConfiguredReason(string? readmeHint)
    {
        const string reason = "UITESTS_Target__BaseUrl is not configured";
        return string.IsNullOrWhiteSpace(readmeHint) ? $"{reason}." : $"{reason}; see {readmeHint}.";
    }

    // Binds a section to a type of the consuming project, once per type and section, e.g. Bind<PlatformTarget>("Target").
    public T Bind<T>(string section)
        where T : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        return (T)extensions.GetOrAdd((typeof(T), section), static (key, configuration) => BindNew<T>(configuration, key.Section), Configuration);
    }

    // An absolute path for a path of the settings, relative to BaseDirectory.
    public string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path, BaseDirectory);
    }

    // The effective settings as "key = value" lines, defaults included and secrets replaced by ***; for the log.
    public string Describe()
    {
        return SettingsRedaction.Describe(this);
    }

    internal void AddWarning(string warning)
    {
        warnings.Add(warning);
    }

    private static T BindNew<T>(IConfiguration configuration, string section)
        where T : class, new()
    {
        var value = new T();
        configuration.GetSection(section).Bind(value);
        return value;
    }
}
