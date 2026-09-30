using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Model;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Output;

public static class VersionsConsoleExtensions
{
    /// <summary>
    /// Writes the versions of a configuration or a template as a table, the newest first.
    /// </summary>
    public static void WriteVersions(this IExtendedConsole @this, IEnumerable<ConfigurationVersion> versions)
    {
        @this.WriteTable(
            versions.OrderByDescending(version => version.Version),
            version => new[]
            {
                version.Version.ToString(CultureInfo.InvariantCulture),
                version.Author,
                FormatTime(version.CreationTime),
                version.IsCurrent() ? "—" : FormatTime(version.ExpirationTime),
            },
            new[] { "Version", "Author", "Created", "Expires" });
    }

    private static string FormatTime(DateTimeOffset time)
    {
        return time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
