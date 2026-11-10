using System.Globalization;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Output;

/// <summary>
/// The line printed after a restore of a version of a configuration, a template or a schema, and the exit code of the
/// command.
/// </summary>
/// <param name="Message">The line to print.</param>
/// <param name="ExitCode"><see cref="ExitCodes.SUCCESS"/> when the API stored a new version, else <see cref="ExitCodes.WARNING_NO_WORK_NEEDED"/>.</param>
public readonly record struct VersionRestoreReport(string Message, int ExitCode)
{
    /// <summary>
    /// Builds the line from the subject of the restore (<c>the configuration for 'rst.billing'</c>) and the response of
    /// the API: <c>Restored</c> names the new version; <c>Unchanged</c> says that the version is the current one, or that
    /// the current version has its content.
    /// </summary>
    public static VersionRestoreReport Create(string subject, VersionRestoreResult result)
    {
        var from = Text(result.FromVersion);
        var version = Text(result.Version);

        if (result.Outcome == VersionRestoreResultOutcome.Restored)
        {
            return new VersionRestoreReport($"Restored v{from} of {subject} as v{version}.", ExitCodes.SUCCESS);
        }

        return result.Version == result.FromVersion
            ? new VersionRestoreReport($"v{from} is already the current version of {subject}.", ExitCodes.WARNING_NO_WORK_NEEDED)
            : new VersionRestoreReport($"v{from} equals the current version v{version} of {subject}; nothing changed.", ExitCodes.WARNING_NO_WORK_NEEDED);
    }

    private static string Text(long version)
    {
        return version.ToString(CultureInfo.InvariantCulture);
    }
}
