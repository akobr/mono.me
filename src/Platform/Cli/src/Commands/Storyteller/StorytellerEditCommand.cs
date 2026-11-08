using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Model;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sharprompt;
using SdkAnnotation = _42.Platform.Storyteller.Sdk.Annotation;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.EDIT, Description = "Edit the content of an annotation in your preferred editor. The saved document replaces the content.")]
public class StorytellerEditCommand : BaseContextCommand
{
    internal const string RestoreAndEditChoice = "Restore and edit (keeps the configuration and the history)";
    internal const string CreateNewChoice = "Create new (starts with an empty configuration)";
    internal const string CancelChoice = "Cancel";

    private readonly IAnnotationsApiClient _annotationsApi;
    private readonly IEditorService _editorService;
    private readonly EditorOptions _editorOptions;
    private readonly IFileSystem _fileSystem;

    public StorytellerEditCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiClient annotationsApi,
        IEditorService editorService,
        IOptions<EditorOptions> editorOptions,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _annotationsApi = annotationsApi;
        _editorService = editorService;
        _editorOptions = editorOptions.Value;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The annotation key to edit.")]
    public string AnnotationKey { get; set; } = string.Empty;

    /// <summary>
    /// Opens the editable content of the annotation (whatever its status) in the configured editor, and replaces the
    /// content with the edited document when the user confirms. A missing key is created; when it or an ancestor is
    /// deleted, the user chooses first whether to restore them.
    /// </summary>
    /// <returns>
    /// An exit code indicating the outcome:
    /// - <c>ExitCodes.SUCCESS</c> when the edited annotation was saved;
    /// - <c>ExitCodes.WARNING_NO_WORK_NEEDED</c> when no changes were made;
    /// - <c>ExitCodes.WARNING_ABORTED</c> when the user aborted the operation;
    /// - <c>ExitCodes.WARNING_INTERACTION_NEEDED</c> when the input is redirected, so the editor cannot ask;
    /// - <c>ExitCodes.ERROR_WRONG_INPUT</c> when the API rejected the annotation (400), a restore failed, or the annotation changed while it was edited;
    /// - <c>ExitCodes.WARNING_UNAUTHORIZED_ACCESS</c> when the API refused a request with 403, with the server's message;
    /// - <c>ExitCodes.ERROR_CRASH</c> when the editor exited with a non-zero code or the API failed otherwise.
    /// After the editor, a failure keeps the edited document in a file and names the command that saves it.
    /// </returns>
    protected override async Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey).WithLowerCaseTypeCode();
        var key = annotationKey.ToString();

        // The editor's questions (Sharprompt) need an interactive console: neither the input nor the output redirected.
        if (Console.Console is { IsInputRedirected: true } or { IsOutputRedirected: true })
        {
            // On standard error, as every prompt that stops on redirected input (InteractiveInputExtensions).
            Console.WriteErrorLine("! sform cannot ask for the edit of the annotation: the input or the output is redirected.");
            Console.WriteErrorLine($"  Without a terminal: sform story get {key} -e <file>, edit the file, then sform story set {key} -i <file> --replace.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        EditSource source;

        try
        {
            var (read, exitCode) = await ReadAsync(annotationKey);

            if (read is null)
            {
                return exitCode;
            }

            source = read;
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            return WriteFailure(e);
        }

        var original = AnnotationEditDocument.Create(annotationKey, source.Annotation);

        // The check normalizes the document, so the diff the user confirms is the content that is written.
        var result = await _editorService.EditJsonAsync(
            Console,
            _editorOptions,
            original,
            $"annotation-{key}",
            source.Annotation is null,
            check: document => AnnotationEditDocument.Check(annotationKey, document));

        if (result.Edited is null)
        {
            if (result.ExitCode == ExitCodes.WARNING_NO_WORK_NEEDED)
            {
                WriteNothingDone(key, source);
            }

            return result.ExitCode;
        }

        return await SaveAsync(annotationKey, source, original, result.Edited);
    }

    // The annotation to edit, or the exit code when the edit must not go on. A write to a deleted key, or below a deleted
    // ancestor, re-creates it empty, so the user chooses first, as in sform story set.
    private async Task<(EditSource? Source, int ExitCode)> ReadAsync(AnnotationKey annotationKey)
    {
        var key = annotationKey.ToString();
        var (stored, etag) = await ReadVersionedAsync(key);

        if (stored is not null)
        {
            return (new EditSource(stored, IsActive: true, Restore: [], etag), ExitCodes.SUCCESS);
        }

        var deleted = await _annotationsApi.FindDeletedAnnotationsAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            new _42.Platform.Storyteller.Sdk.AnnotationKeysRequest
            {
                Keys = annotationKey.GetAllAncestorKeys().Append(annotationKey).Select(ancestor => ancestor.ToString()).ToList(),
            });

        if (deleted is not { Count: > 0 })
        {
            Console.WriteLine($"Annotation '{key}' does not exist yet, creating new.");
            return (new EditSource(null, IsActive: false, Restore: []), ExitCodes.SUCCESS);
        }

        Console.WriteLine(deleted.Count == 1 ? "This annotation is deleted:" : "These annotations are deleted:");

        foreach (var annotation in deleted)
        {
            Console.WriteLine(annotation.Deletion is { } deletion
                ? $"- {annotation.AnnotationKey} ({AuditConsoleExtensions.FormatDeletion(deletion)})"
                : $"- {annotation.AnnotationKey}");
        }

        var choice = Console.Select(new SelectOptions<string>
        {
            Message = "What do you want to do",
            Items = new[] { RestoreAndEditChoice, CreateNewChoice, CancelChoice },
            DefaultValue = RestoreAndEditChoice,
        });

        if (choice == CancelChoice)
        {
            Console.WriteLine("Nothing was changed.");
            return (null, ExitCodes.WARNING_ABORTED);
        }

        if (choice == CreateNewChoice)
        {
            return (new EditSource(null, IsActive: false, Restore: []), ExitCodes.SUCCESS);
        }

        // The deleted record of the key itself, read on its own: the lookup answers the members of the base type only,
        // without a unit's type and definition, for example. A key below a deleted ancestor starts empty.
        var record = deleted.Any(annotation => annotation.AnnotationKey == key)
            ? await _annotationsApi.TryGetDeletedAnnotationAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key)
            : null;

        return (new EditSource(record, IsActive: false, Restore: deleted.Select(annotation => annotation.AnnotationKey).ToList()), ExitCodes.SUCCESS);
    }

    // The guard, the restore and the write. Whatever fails keeps the edit in a file, so it is not lost.
    private async Task<int> SaveAsync(AnnotationKey annotationKey, EditSource source, JObject original, JObject edited)
    {
        var key = annotationKey.ToString();
        var restored = false;

        try
        {
            // Built first, so a body that cannot be built changes nothing.
            var body = AnnotationEditDocument.ToAnnotation(annotationKey, Context.GetFullKey(annotationKey), edited).ToSdkAnnotation();

            // Another write while the editor was open must not be lost. The write is conditioned on the version the
            // edit opened (If-Match), or on the key still missing (If-None-Match: *); only a read without an ETag (an
            // older API) and a restore, which makes a new version, are checked by reading again first.
            if (NeedsReadCheck(source) && await HasChangedAsync(annotationKey, source, original))
            {
                return Changed(key, edited, restored);
            }

            if (source.Restore.Count > 0)
            {
                (var allRestored, restored) = await RestoreAsync(source.Restore);

                if (!allRestored)
                {
                    KeepEdit(key, edited, restored);
                    return ExitCodes.ERROR_WRONG_INPUT;
                }
            }

            if (!await TryWriteAsync(annotationKey, body, source, original))
            {
                return Changed(key, edited, restored);
            }

            Console.WriteImportant($"Annotation '{key}' has been saved.");
            return ExitCodes.SUCCESS;
        }
        catch (JsonException e)
        {
            // The client refused to write the body, before any request: a defect of sform, not of the input.
            Console.WriteErrorLine($"The annotation {key} could not be sent: {e.Message}");
            KeepEdit(key, edited, restored);
            return ExitCodes.ERROR_CRASH;
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            var exitCode = WriteFailure(e);
            KeepEdit(key, edited, restored);
            return exitCode;
        }
        catch
        {
            // a 401 or a network error: the top level reports it, after the edit is kept
            KeepEdit(key, edited, restored);
            throw;
        }
    }

    // The write under its condition; false when the annotation is not what the edit opened. A version that changed only
    // in what the document does not show (the relation arrays: a child created or deleted) is written over, once.
    private async Task<bool> TryWriteAsync(AnnotationKey annotationKey, SdkAnnotation body, EditSource source, JObject original)
    {
        try
        {
            await WriteAsync(annotationKey.ToString(), body, source);
            return true;
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.PreconditionFailed && source.IsActive)
        {
            var (current, etag) = await ReadVersionedAsync(annotationKey.ToString());

            if (current is null
                || string.IsNullOrEmpty(etag)
                || !JToken.DeepEquals(AnnotationEditDocument.Create(annotationKey, current), original))
            {
                return false;
            }

            try
            {
                await WriteAsync(annotationKey.ToString(), body, source with { ETag = etag });
                return true;
            }
            catch (ApiException again) when (again.StatusCode == (int)HttpStatusCode.PreconditionFailed)
            {
                return false;
            }
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.PreconditionFailed)
        {
            // a key created meanwhile, or restored after the check of a restore
            return false;
        }
    }

    // The write cannot carry the condition: a read without an ETag, or a restore, whose new version the edit never saw.
    private static bool NeedsReadCheck(EditSource source)
    {
        return source.Restore.Count > 0 || (source.IsActive && string.IsNullOrEmpty(source.ETag));
    }

    private int Changed(string key, JObject edited, bool restored)
    {
        Console.WriteImportant($"The annotation '{key}' changed while you edited it, so your edit was not saved.");
        KeepEdit(key, edited, restored);
        return ExitCodes.ERROR_WRONG_INPUT;
    }

    // The annotation whatever its status, with the ETag of its version; (null, null) when it is missing or deleted.
    private async Task<(SdkAnnotation? Annotation, string? ETag)> ReadVersionedAsync(string key)
    {
        try
        {
            var read = await _annotationsApi.GetAnnotationVersionedAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key, includeInactive: true);
            return (read.Result, read.ETag);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return (null, null);
        }
    }

    // Whether the annotation is not what the edit started from: its content changed, or it appeared or went away.
    private async Task<bool> HasChangedAsync(AnnotationKey annotationKey, EditSource source, JObject original)
    {
        var current = await ReadActiveAsync(annotationKey.ToString());

        return source.IsActive
            ? current is null || !JToken.DeepEquals(AnnotationEditDocument.Create(annotationKey, current), original)
            : current is not null;
    }

    // The annotation whatever its status (disabled, scheduled and expired ones too); null when it is missing or deleted.
    private Task<SdkAnnotation?> ReadActiveAsync(string key)
    {
        return _annotationsApi.TryGetAnnotationAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key);
    }

    // A replace (not a merge): what the document does not hold is cleared on the server as well. Only of the version the
    // edit opened, or only a create of a key that is still missing; after a restore, without a condition (checked before).
    private Task<SdkAnnotation> WriteAsync(string key, SdkAnnotation body, EditSource source)
    {
        var ifMatch = source.IsActive && !string.IsNullOrEmpty(source.ETag) ? source.ETag : null;
        var ifNoneMatch = !source.IsActive && source.Restore.Count == 0 ? "*" : null;

        return _annotationsApi.SetAnnotationAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            key,
            body,
            ifMatch,
            ifNoneMatch);
    }

    // Restores the deleted keys alone, right before the write: whether all of them were restored, and whether any was.
    // The server restores them one by one and keeps what it restored when a later key fails.
    private async Task<(bool All, bool Any)> RestoreAsync(IReadOnlyList<string> keys)
    {
        var result = await _annotationsApi.RestoreAnnotationsAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            new _42.Platform.Storyteller.Sdk.AnnotationRestoreRequest
            {
                Keys = keys.ToList(),
                Descendants = false,
            });

        var restored = (result.Annotations ?? []).Select(annotation => annotation.AnnotationKey).ToList();

        if (restored.Count > 0)
        {
            Console.WriteLine($"Restored {string.Join(", ", restored)}.");
        }

        foreach (var failure in result.Failed ?? [])
        {
            Console.WriteLine($"The annotation '{failure.Key}' was not restored: {failure.Message}");
        }

        return (result.Failed is not { Count: > 0 }, restored.Count > 0);
    }

    // As sform story set ends a failed request.
    private int WriteFailure(ApiException exception)
    {
        switch (exception.StatusCode)
        {
            case (int)HttpStatusCode.BadRequest:
            case (int)HttpStatusCode.Conflict:
                // for example InvalidAnnotation: the server names the field and its limit
                Console.WriteLine(exception.GetErrorMessage());
                return exception.StatusCode == (int)HttpStatusCode.BadRequest ? ExitCodes.ERROR_WRONG_INPUT : ExitCodes.ERROR_CRASH;

            case (int)HttpStatusCode.Forbidden:
                // for example a machine without Annotation.Write, or without Annotation.Manage for the restore
                return Console.WriteApiError(exception);

            default:
                Console.WriteLine($"Error occurred: {exception.Message}");
                return ExitCodes.ERROR_CRASH;
        }
    }

    // The edited document in a new file, and the command that saves it without the editor: set replaces the content
    // with the same members.
    private void KeepEdit(string key, JObject edited, bool restored)
    {
        if (restored)
        {
            Console.WriteLine("The restored annotations keep the content they had when they were deleted.");
        }

        try
        {
            var path = EditorService.CreateTempFilePath(_fileSystem, $"annotation-{key}");
            _fileSystem.File.WriteAllText(path, edited.ToString(Formatting.Indented), new UTF8Encoding(false));
            Console.WriteLine($"Your edit is in {path}. To save it anyway: sform story set {key} -i \"{path}\" --replace");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteImportant($"Your edit could not be kept in a file: {e.Message}");
        }
    }

    // After an edit without changes: what did not happen, and the commands that do it without the editor.
    private void WriteNothingDone(string key, EditSource source)
    {
        if (source.Restore.Count > 0)
        {
            Console.WriteLine(source.Restore.Count == 1
                ? $"Nothing was restored. To restore it as it was: sform story restore {source.Restore[0]} --without-descendants"
                : $"Nothing was restored. To restore them as they were: sform story restore {string.Join(" ", source.Restore)} --without-descendants");
        }

        if (source.Annotation is null)
        {
            Console.WriteLine($"Nothing was created. To create it without content: sform story set {key}");
        }
    }

    // What the edit starts from: the annotation (null for an empty document), whether it is the active one, the deleted
    // keys to restore before the write, and the ETag of the version opened (an active annotation).
    private sealed record class EditSource(SdkAnnotation? Annotation, bool IsActive, IReadOnlyList<string> Restore, string? ETag = null);
}
