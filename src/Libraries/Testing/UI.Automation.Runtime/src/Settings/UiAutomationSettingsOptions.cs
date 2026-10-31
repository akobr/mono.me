using System.IO.Abstractions;
using System.Reflection;

namespace _42.Testing.UI.Automation.Runtime.Settings;

// What UiAutomationSettingsLoader reads. The engine adapter fills it: the test assembly for the user secrets, its
// command-line parameters and the consumer's setup.
public sealed class UiAutomationSettingsOptions
{
    // Where testsettings*.json are; AppContext.BaseDirectory (the output directory of the test assembly) when unset.
    public string? BaseDirectory { get; set; }

    // The name of the files without extension: <FileName>.json, <FileName>.<profile>.json, <FileName>.local.json.
    public string FileName { get; set; } = "testsettings";

    // The assembly whose UserSecretsIdAttribute names the user secrets: the test assembly, never guessed by a library.
    public Assembly? UserSecretsAssembly { get; set; }

    // Wins over UserSecretsAssembly.
    public string? UserSecretsId { get; set; }

    // Wins over the Profile parameter and UITESTS_PROFILE; several profiles are separated by commas, applied in order.
    public string? Profile { get; set; }

    // The engine's command-line parameters, e.g. TUnit's --test-parameter Browser:Headless=false: the highest layer.
    // "__" in a key works like ":".
    public IEnumerable<KeyValuePair<string, string?>>? Parameters { get; set; }

    // The process environment when unset; only UITESTS_* variables are read.
    public IReadOnlyDictionary<string, string?>? EnvironmentVariables { get; set; }

    // Lets the loader check Target:SignIn and the pool passwords against the registered flows, and runs the consumer's
    // own validation.
    public IUiAutomationSetup? Setup { get; set; }

    // A FileSystem over System.IO when unset.
    public IFileSystem? FileSystem { get; set; }
}
