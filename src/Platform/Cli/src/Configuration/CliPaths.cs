using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using _42.Platform.Cli.Output.Exceptions;
using Microsoft.Identity.Client.Extensions.Msal;

namespace _42.Platform.Cli.Configuration;

// Where sform keeps its files, resolved once per process (Program.Main), so that no other code builds a path from the
// location of the assembly or the user profile:
// - ApplicationDirectory, the folder of the assembly: app.config.json, always;
// - StateDirectory: access.default.json, editor.config.json, auth.discovery.json and the log files (logs/). Without
//   SFORM_HOME the folder of the assembly, as before;
// - UserDirectory: the AuthKit session, the Entra ID MSAL cache and telemetry.json. Without SFORM_HOME the user's
//   .42for.net folder (%LOCALAPPDATA%\.42for.net on Windows, ~/.42for.net elsewhere), which survives tool updates.
// With SFORM_HOME both are SFORM_HOME. SFORM_HOME must be an absolute path; a relative one, or one that names a file,
// is an input error. A directory that does not exist yet is created by the first write.
public sealed class CliPaths
{
    public const string USER_DIRECTORY_NAME = UserSettingsLocation.DIRECTORY_NAME;
    public const string DEFAULT_LOG_FILE_PATH = "logs/cli.log";

    private readonly IFileSystem _fileSystem;
    private readonly Lazy<string> _userDirectory;

    private CliPaths(IFileSystem fileSystem, string applicationDirectory, string? home, Func<string> userRootDirectory)
    {
        _fileSystem = fileSystem;
        ApplicationDirectory = fileSystem.Path.TrimEndingDirectorySeparator(applicationDirectory);
        Home = home;
        StateDirectory = home ?? ApplicationDirectory;

        // Read only when needed: with SFORM_HOME the user profile is never touched.
        _userDirectory = new Lazy<string>(() => home ?? fileSystem.Path.Combine(userRootDirectory(), USER_DIRECTORY_NAME));
    }

    public string ApplicationDirectory { get; }

    // The full path of SFORM_HOME, or null when it is not set.
    public string? Home { get; }

    public bool IsHomeSet => Home is not null;

    public string StateDirectory { get; }

    public string UserDirectory => _userDirectory.Value;

    public string AccessDefaultFile => _fileSystem.Path.Combine(StateDirectory, Constants.ACCESS_DEFAULT_JSON);

    public string EditorConfigFile => _fileSystem.Path.Combine(StateDirectory, Constants.EDITOR_CONFIG_JSON);

    public string AuthDiscoveryFile => _fileSystem.Path.Combine(StateDirectory, Constants.AUTH_DISCOVERY_JSON);

    // sform.settings.json, the user's settings (the editor and the default project; IUserSettingsStore).
    public string UserSettingsFile => _fileSystem.Path.Combine(UserDirectory, Constants.USER_SETTINGS_JSON);

    // The files of earlier versions by the section each one held, read until the settings file has that section.
    public IReadOnlyDictionary<string, string> LegacySettingsFiles => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [ConfigurationSections.ACCESS] = AccessDefaultFile,
        [ConfigurationSections.EDITOR] = EditorConfigFile,
    };

    // Throws a WrongInputException with the reason when SFORM_HOME is set to something sform cannot use.
    public static CliPaths FromEnvironment(IFileSystem fileSystem, Func<string, string?> getVariable)
    {
        return Create(fileSystem, getVariable(SformVariables.HOME), AppContext.BaseDirectory, () => MsalCacheHelper.UserRootDirectory);
    }

    // The log file template of logging.logFilePath (logs/cli.log by default), under the state directory unless absolute.
    public string GetLogFilePath(string? configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath) ? DEFAULT_LOG_FILE_PATH : configuredPath;
        return _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(StateDirectory, path));
    }

    // The file the daily rolling sink writes on the given day: logs/cli20261108.log.
    public string GetDailyLogFilePath(string? configuredPath, DateTime day)
    {
        var template = GetLogFilePath(configuredPath);
        var directory = _fileSystem.Path.GetDirectoryName(template) ?? StateDirectory;
        var name = $"{_fileSystem.Path.GetFileNameWithoutExtension(template)}{day:yyyyMMdd}{_fileSystem.Path.GetExtension(template)}";
        return _fileSystem.Path.Combine(directory, name);
    }

    // Before a write: SFORM_HOME, or a folder below it, may not exist yet.
    public void EnsureDirectoryOf(string filePath)
    {
        var directory = _fileSystem.Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }
    }

    internal static CliPaths Create(IFileSystem fileSystem, string? home, string applicationDirectory, Func<string> userRootDirectory)
    {
        return new CliPaths(fileSystem, applicationDirectory, ResolveHome(fileSystem, home), userRootDirectory);
    }

    private static string? ResolveHome(IFileSystem fileSystem, string? home)
    {
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        var value = home.Trim();

        if (!fileSystem.Path.IsPathFullyQualified(value))
        {
            throw new WrongInputException($"{SformVariables.HOME} must be an absolute path, and '{value}' is relative.");
        }

        var fullPath = fileSystem.Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(value));

        if (fileSystem.File.Exists(fullPath))
        {
            throw new WrongInputException($"{SformVariables.HOME} must be a directory, and '{fullPath}' is a file.");
        }

        return fullPath;
    }
}
