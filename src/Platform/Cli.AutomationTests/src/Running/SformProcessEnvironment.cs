using System.Collections;

namespace _42.Platform.Cli.AutomationTests.Running;

// The environment a run of sform starts with, built from scratch: the variables a .NET process needs from the machine
// (Inherited, copied when set), then what every run sets (Fixed and SFORM_HOME), then the run's own (a machine's
// STORYTELLER_* variables, the test's additions; null removes). Nothing else of the machine that runs the tests reaches
// sform: not the outer STORYTELLER_*, SFORM_*, BROWSER, DOTNET_ENVIRONMENT or ASPNETCORE_ENVIRONMENT, not GITHUB_ENV or
// the GitHub Actions token variables of a CI job, not a developer's user secrets, so a test sees what a clean shell sees.
public static class SformProcessEnvironment
{
    // SFORM_HOME, set for every run but one of SformRunOptions.WithoutHome: sform never touches the real profile or the
    // folder of its build.
    public const string Home = "SFORM_HOME";

    // A tag of each started process, inherited by everything it starts, so the runner finds what sform started that
    // outlived it (a browser opener that keeps the standard streams open) where there is no job object (Linux).
    public const string ProcessTag = "CLI_AUTOMATION_PROCESS_TAG";

    public const string TokenStore = "SFORM_TOKEN_STORE";

    public const string DeviceCodeOutput = "SFORM_DEVICE_CODE_OUTPUT";

    public const string BaseUrl = "SFORM_general__baseUrl";

    public const string Browser = "SFORM_BROWSER";

    public const string GitHubEnv = "GITHUB_ENV";

    // What a process needs from the machine to start and to reach the network: the system folders and the shell's
    // search path, the temporary and profile folders (the dotnet host and the runtime read them; sform itself reads
    // nothing of the profile with SFORM_HOME), the dotnet installation and package folders, the locale, and the proxies
    // and certificate bundles of a CI agent.
    public static IReadOnlyList<string> Inherited { get; } =
    [
        "PATH", "PATHEXT", "SystemRoot", "SystemDrive", "windir", "ComSpec", "OS",
        "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "NUMBER_OF_PROCESSORS", "COMPUTERNAME",
        "TEMP", "TMP", "TMPDIR",
        "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "HOME", "USERNAME", "USER", "LOGNAME", "USERDOMAIN",
        "LOCALAPPDATA", "APPDATA", "ProgramData", "ALLUSERSPROFILE", "PUBLIC",
        "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432",
        "DOTNET_ROOT", "DOTNET_ROOT(x86)", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_CLI_HOME",
        "DOTNET_HOST_PATH", "DOTNET_MULTILEVEL_LOOKUP", "DOTNET_ROLL_FORWARD", "NUGET_PACKAGES",
        "LANG", "LANGUAGE", "TZ",
        "SSL_CERT_FILE", "SSL_CERT_DIR",
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "no_proxy", "all_proxy",
    ];

    // The prefixes of inherited families: the locale categories.
    public static IReadOnlyList<string> InheritedPrefixes { get; } = ["LC_"];

    // Set for every run: production configuration (no user secrets of a build from source), no telemetry, plain and wide
    // output, no first-run banner of the dotnet host.
    public static IReadOnlyDictionary<string, string> Fixed { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DOTNET_ENVIRONMENT"] = "Production",
        ["SFORM_TELEMETRY_OPTOUT"] = "1",
        ["DO_NOT_TRACK"] = "1",
        ["NO_COLOR"] = "1",
        ["COLUMNS"] = "1000",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
    };

    // The whitelisted variables of the given source (the process's environment when null).
    public static Dictionary<string, string> FromMachine(IDictionary? source = null)
    {
        source ??= System.Environment.GetEnvironmentVariables();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var wanted = new HashSet<string>(Inherited, comparer);
        var result = new Dictionary<string, string>(comparer);
        foreach (DictionaryEntry entry in source)
        {
            if (entry.Key is not string name || entry.Value is not string value || value.Length == 0)
            {
                continue;
            }

            if (wanted.Contains(name) || InheritedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                result[name] = value;
            }
        }

        return result;
    }

    // The environment of a run: the machine's whitelist, Fixed, SFORM_HOME, then the overrides in order (null removes).
    // withoutHome (SformRunOptions.WithoutHome) leaves SFORM_HOME out and, but on Windows, points HOME and the XDG
    // folders at home instead, so sform's user folder is a throw-away one; windows: null for the running system.
    public static Dictionary<string, string> Build(
        string home,
        IEnumerable<IReadOnlyDictionary<string, string?>> overrides,
        IDictionary? machine = null,
        bool withoutHome = false,
        bool? windows = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(overrides);

        var environment = FromMachine(machine);
        foreach (var (name, value) in Fixed)
        {
            environment[name] = value;
        }

        environment[Home] = home;
        foreach (var layer in overrides)
        {
            foreach (var (name, value) in layer)
            {
                if (value is null)
                {
                    environment.Remove(name);
                }
                else
                {
                    environment[name] = value;
                }
            }
        }

        if (!withoutHome)
        {
            // SFORM_HOME is not optional unless asked: a run without it uses the real profile and the build folder.
            environment[Home] = home;
            return environment;
        }

        environment.Remove(Home);
        if (!(windows ?? OperatingSystem.IsWindows()))
        {
            // MSAL's user folder on Linux and macOS is HOME; the keyring and the .NET folders follow the XDG ones.
            environment["HOME"] = home;
            environment["XDG_CONFIG_HOME"] = Path.Combine(home, ".config");
            environment["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share");
            environment["XDG_STATE_HOME"] = Path.Combine(home, ".local", "state");
            environment["XDG_CACHE_HOME"] = Path.Combine(home, ".cache");
        }

        return environment;
    }
}
