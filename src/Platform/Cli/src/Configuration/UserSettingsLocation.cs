using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using Microsoft.Identity.Client.Extensions.Msal;

namespace _42.Platform.Cli.Configuration;

// What sform keeps for the user lives in the .42for.net folder: %LOCALAPPDATA%\.42for.net on Windows, ~/.42for.net
// elsewhere. It holds the stored session, the telemetry id and the settings, and survives tool updates.
public static class UserSettingsLocation
{
    public const string DIRECTORY_NAME = ".42for.net";

    // Throws InvalidOperationException when the folder of the user is unknown (no HOME nor user name on Linux or macOS).
    public static string GetDirectory(IFileSystem fileSystem)
    {
        var root = MsalCacheHelper.UserRootDirectory;

        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException("The folder of the user is unknown; set HOME.");
        }

        return fileSystem.Path.Combine(root, DIRECTORY_NAME);
    }

    public static string GetFilePath(IFileSystem fileSystem)
    {
        return fileSystem.Path.Combine(GetDirectory(fileSystem), Constants.USER_SETTINGS_JSON);
    }

    // The files of earlier versions, by the section each one held: next to the assembly, in the content root that the
    // configuration read them from. sform only reads them now, and moves them into the settings file on the first change.
    public static IReadOnlyDictionary<string, string> GetLegacyFilePaths(IFileSystem fileSystem)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConfigurationSections.ACCESS] = fileSystem.Path.Combine(AppContext.BaseDirectory, Constants.ACCESS_DEFAULT_JSON),
            [ConfigurationSections.EDITOR] = fileSystem.Path.Combine(AppContext.BaseDirectory, Constants.EDITOR_CONFIG_JSON),
        };
    }
}
