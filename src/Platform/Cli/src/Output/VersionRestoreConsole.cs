using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Output;

/// <summary>
/// The restore commands of a version (<c>config restore</c>, <c>template restore</c>, <c>schema … restore</c>): the check
/// of <c>--version</c>, the call and what it prints.
/// </summary>
public static class VersionRestoreConsole
{
    public const string MissingVersionMessage = "Please specify the version to restore with --version.";

    /// <summary>
    /// The version of <c>--version</c>. Without it, or with a version below 1 (versions start at 1), writes why and throws
    /// a <see cref="WrongInputException"/> before any call.
    /// </summary>
    public static int ValidateVersionToRestore(this IExtendedConsole @this, int? version)
    {
        if (version is not { } value)
        {
            @this.WriteImportant(MissingVersionMessage);
            throw new WrongInputException();
        }

        if (value < 1)
        {
            @this.WriteImportant($"The version to restore must be 1 or more, not {value.ToString(CultureInfo.InvariantCulture)}; 'versions' lists them.");
            throw new WrongInputException();
        }

        return value;
    }

    /// <summary>
    /// Runs the restore and prints its outcome (<see cref="VersionRestoreReport"/>): 0 when a new version was stored, 42
    /// when nothing changed. A refusal of the API prints the failing configurations of a schema violation (2), else the
    /// server's message and error code (<c>VersionNotFound</c>, <c>AnnotationNotFound</c>, <c>AnnotationDeleted</c>,
    /// <c>Conflict</c>) with the exit code of its status; a 401 is left to <c>Program</c>.
    /// </summary>
    /// <param name="console">The console of the command.</param>
    /// <param name="subject">What is restored, as the messages name it: <c>the template for 'unt'</c>.</param>
    /// <param name="restore">The call of the API.</param>
    public static async Task<int> RestoreVersionAsync(this IExtendedConsole console, string subject, Func<Task<VersionRestoreResult>> restore)
    {
        VersionRestoreResult result;

        try
        {
            result = await restore();
        }
        catch (ApiException exception) when (exception.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            return SchemaValidationConsole.WriteWriteError(console, exception);
        }

        var report = VersionRestoreReport.Create(subject, result);
        console.WriteImportant(report.Message);
        return report.ExitCode;
    }
}
