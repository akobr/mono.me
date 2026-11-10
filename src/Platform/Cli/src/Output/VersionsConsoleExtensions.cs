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
    /// Writes the versions of a configuration, a template or a schema as a table, the newest first. When a listed version
    /// was stored by a restore, a last column <c>Restored from</c> names the version it copied (<c>v2</c>, else <c>—</c>).
    /// </summary>
    public static void WriteVersions(this IExtendedConsole @this, IEnumerable<ConfigurationVersion> versions)
    {
        var rows = versions.OrderByDescending(version => version.Version).ToList();
        var showsRestoredFrom = rows.Any(version => version.RestoredFrom is not null);
        var headers = new List<string> { "Version", "Author", "Created", "Expires" };

        if (showsRestoredFrom)
        {
            headers.Add("Restored from");
        }

        @this.WriteTable(
            rows,
            version => Row(version, showsRestoredFrom),
            headers);
    }

    private static string[] Row(ConfigurationVersion version, bool showsRestoredFrom)
    {
        var cells = new List<string>
        {
            version.Version.ToString(CultureInfo.InvariantCulture),
            AuditConsoleExtensions.FormatActor(version.Modified?.By),
            FormatTime(version.Modified?.At),
            version.IsCurrent() ? "—" : FormatTime(version.ExpirationTime),
        };

        if (showsRestoredFrom)
        {
            cells.Add(version.RestoredFrom is { } restoredFrom ? $"v{restoredFrom.ToString(CultureInfo.InvariantCulture)}" : "—");
        }

        return [.. cells];
    }

    private static string FormatTime(DateTimeOffset? time)
    {
        return time is null ? "—" : time.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
