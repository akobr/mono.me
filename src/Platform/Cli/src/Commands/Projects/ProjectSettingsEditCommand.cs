using System;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.Commands.Projects;

[Command(CommandNames.EDIT, Description = "Edit the settings of the project in your preferred editor; the saved document replaces them. Needs the Administrator role.")]
public class ProjectSettingsEditCommand : BaseContextCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IEditorService _editorService;
    private readonly EditorOptions _editorOptions;
    private readonly IFileSystem _fileSystem;
    private readonly IProjectSettingsCache _cache;

    public ProjectSettingsEditCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IEditorService editorService,
        IOptions<EditorOptions> editorOptions,
        IFileSystem fileSystem,
        IProjectSettingsCache cache)
        : base(console, context)
    {
        _accessApi = accessApi;
        _editorService = editorService;
        _editorOptions = editorOptions.Value;
        _fileSystem = fileSystem;
        _cache = cache;
    }

    /// <summary>
    /// Opens the settings in the file shape (the Format marker, Types, DefaultTimeZone, DateFormat) in the configured
    /// editor, checks the edited document by the shared rules, shows the changes and, when the user confirms, replaces the
    /// settings with it, only while they are at the version that was opened (If-Match).
    /// </summary>
    /// <returns>
    /// An exit code indicating the outcome:
    /// - <c>ExitCodes.SUCCESS</c> when the edited settings were saved;
    /// - <c>ExitCodes.WARNING_NO_WORK_NEEDED</c> when nothing changed;
    /// - <c>ExitCodes.WARNING_ABORTED</c> when the user aborted the edit;
    /// - <c>ExitCodes.WARNING_INTERACTION_NEEDED</c> when the input or the output is redirected, so the editor cannot ask;
    /// - <c>ExitCodes.ERROR_WRONG_INPUT</c> when the edited settings break a rule, or changed meanwhile (412);
    /// - <c>ExitCodes.WARNING_UNAUTHORIZED_ACCESS</c> when the API refused the change (403);
    /// - <c>ExitCodes.ERROR_CRASH</c> when the editor exited with a non-zero code.
    /// After the editor, a failure keeps the edited document in a file and names the command that saves it under the
    /// version it was opened at; after a 412 it says to merge the edit into the current settings instead.
    /// </returns>
    protected override async Task<int> ExecuteAsync()
    {
        var projectKey = ProjectSettingsCommands.ProjectKeyOf(Context);

        // The editor's questions (Sharprompt) need an interactive console: neither the input nor the output redirected.
        if (Console.Console is { IsInputRedirected: true } or { IsOutputRedirected: true })
        {
            Console.WriteErrorLine("! sform cannot ask for the edit of the project settings: the input or the output is redirected.");
            Console.WriteErrorLine($"  Without a terminal: sform project settings -p {projectKey} -e <file> (note its Version), edit the file, then sform project settings set -p {projectKey} -i <file> --replace --expected-version <version>.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        SdkSettings before;
        string etag;

        try
        {
            (before, etag) = await ProjectSettingsCommands.ReadAsync(_accessApi, Context);
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            return Console.WriteApiError(e);
        }

        var original = ProjectSettingsDocuments.ToFileDocument(ProjectSettingsDocuments.ToModel(before));

        var result = await _editorService.EditJsonAsync(
            Console,
            _editorOptions,
            original,
            $"project-settings-{projectKey}",
            isNew: false,
            check: Check);

        if (result.Edited is null)
        {
            // Given up after an edit that is not JSON or that the rules refused (no re-open, or no terminal to ask): the
            // text of that edit is not lost.
            if (result.Rejected is { } refused && result.ExitCode is ExitCodes.WARNING_ABORTED or ExitCodes.ERROR_WRONG_INPUT)
            {
                KeepEdit(projectKey, refused, before.Version, "Fix it, then save it with");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            // The editor failed on a re-open of a refused draft: the draft is kept as well, and the crash is the outcome.
            if (result.Rejected is { } draft && result.ExitCode == ExitCodes.ERROR_CRASH)
            {
                KeepEdit(projectKey, draft, before.Version, "It is the last draft that was checked; what the failed editor held after it was not read. Fix it, then save it with");
                return ExitCodes.ERROR_CRASH;
            }

            return result.ExitCode;
        }

        return await SaveAsync(projectKey, before, etag, result.Edited);
    }

    private static string Text(JObject document)
    {
        return document.ToString(Formatting.Indented);
    }

    // The write of the edited settings under the version that was opened. Whatever fails keeps the edit in a file.
    private async Task<int> SaveAsync(string projectKey, SdkSettings before, string etag, JObject edited)
    {
        var check = ProjectSettingsJson.Read(ProjectSettingsDocuments.ToNode(edited));

        if (!check.IsValid)
        {
            Console.WriteSettingsProblems(check.Problems);
            KeepEdit(projectKey, Text(edited), before.Version, "Fix it, then save it with");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        VersionedResponse<SdkSettings> response;

        try
        {
            response = await _accessApi.SetProjectSettingsVersionedAsync(
                Context.OrganizationName,
                Context.ProjectName,
                ProjectSettingsDocuments.ToSdk(check.Settings),
                etag);
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            var exitCode = Console.WriteSettingsFailure(e);

            if (e.StatusCode == (int)HttpStatusCode.PreconditionFailed)
            {
                // The edit started from settings that are gone: saving it as it is would undo the other change.
                KeepStaleEdit(projectKey, Text(edited));
            }
            else
            {
                KeepEdit(projectKey, Text(edited), before.Version, "To save it anyway");
            }

            return exitCode;
        }
        catch
        {
            // a 401 or a network error: the top level reports it, after the edit is kept
            KeepEdit(projectKey, Text(edited), before.Version, "To save it anyway");
            throw;
        }

        var after = response.Result;
        await _cache.RememberAsync(Context.OrganizationName, Context.ProjectName, after, response.ETag ?? ProjectSettingsDocuments.ETagOf(after.Version));

        // The changes were shown before the confirmation.
        return ProjectSettingsCommands.WriteOutcome(Console, before, after, showChanges: false);
    }

    // The edited document by the shared rules: normalized (so the diff the user confirms is what is written), or every
    // problem printed and the editor offered again.
    private JsonEditCheck Check(JObject document)
    {
        var check = ProjectSettingsJson.Read(ProjectSettingsDocuments.ToNode(document));

        if (check.IsValid)
        {
            return JsonEditCheck.Valid(ProjectSettingsDocuments.ToFileDocument(check.Settings));
        }

        Console.WriteSettingsProblems(check.Problems);
        return JsonEditCheck.Invalid("The edited project settings are not valid.");
    }

    // The edited document in a new file, and the command that saves it without the editor, only while the settings are
    // still at the version the edit started from.
    private void KeepEdit(string projectKey, string edited, long version, string lead)
    {
        if (WriteEdit(projectKey, edited) is { } path)
        {
            Console.WriteLine($"Your edit is in {path}. {lead}: sform project settings set -p {projectKey} -i \"{path}\" --replace --expected-version {version}");
        }
    }

    // The edit of settings that changed meanwhile: kept, with the advice to merge it into the current settings.
    private void KeepStaleEdit(string projectKey, string edited)
    {
        if (WriteEdit(projectKey, edited) is { } path)
        {
            Console.WriteLine($"Your edit is in {path}. It started from an older version: read the current settings with sform project settings -p {projectKey} -e <file>, merge your changes into that file, and save it with sform project settings set -p {projectKey} -i <file> --replace --expected-version <the version it shows>.");
        }
    }

    private string? WriteEdit(string projectKey, string edited)
    {
        try
        {
            var path = EditorService.CreateTempFilePath(_fileSystem, $"project-settings-{projectKey}");
            _fileSystem.File.WriteAllText(path, edited, new UTF8Encoding(false));
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteImportant($"Your edit could not be kept in a file: {e.Message}");
            return null;
        }
    }
}
