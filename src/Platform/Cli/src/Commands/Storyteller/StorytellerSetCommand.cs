using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
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

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create an annotation, or merge into an existing one; --replace clears what the file and the options do not hold.")]
public class StorytellerSetCommand : BaseContextCommand
{
    internal const string RestoreAndUpdateChoice = "Restore and update (keeps the configuration and the history)";
    internal const string CreateNewChoice = "Create new (starts with an empty configuration)";
    internal const string CancelChoice = "Cancel";

    // The rounds of a merge: a patch of a missing key and its create, which fails when the key is created meanwhile.
    internal const int MaxWriteRounds = 3;

    private const string AnyVersion = "*";
    private const string AnnotationNotFound = "AnnotationNotFound";
    private const string AnnotationDeleted = "AnnotationDeleted";

    private readonly AccessDefaultOptions _accessDefault;
    private readonly IAnnotationsApiClient _annotationsApi;
    private readonly IFileSystem _fileSystem;

    public StorytellerSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiClient annotationsApi,
        IOptions<AccessDefaultOptions> accessDefaultOptions,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _accessDefault = accessDefaultOptions.Value;
        _annotationsApi = annotationsApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The annotation key to create or update.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-i|--import", CommandOptionType.SingleValue, Description = "A JSON file with the content of the annotation, for example one saved with sform story get -e.")]
    public string? ImportFilePath { get; set; }

    public bool IsImportRequested => !string.IsNullOrWhiteSpace(ImportFilePath);

    [Option("-c|--custom-properties", CommandOptionType.MultipleValue, Description = "A value of the annotation, as name=value. It wins over the same name in the file.")]
    public string[]? CustomProperties { get; set; }

    public bool AreCustomPropertiesSpecified => CustomProperties?.Length > 0;

    [Option("-l|--labels", CommandOptionType.MultipleValue, Description = "A label of the annotation, added to those of the file.")]
    public string[]? Labels { get; set; }

    public bool AreLabelsSpecified => Labels?.Length > 0;

    [Option("--restore-deleted", CommandOptionType.NoValue, Description = "When the annotation or an ancestor is deleted, restore it (with its configuration and history) and update it.")]
    public bool RestoreDeleted { get; set; }

    [Option("--recreate", CommandOptionType.NoValue, Description = "When the annotation or an ancestor is deleted, create it new, with an empty configuration.")]
    public bool Recreate { get; set; }

    [Option("--replace", CommandOptionType.NoValue, Description = "Replace the content of an existing annotation instead of merging into it: what the file and the options do not hold is cleared.")]
    public bool IsReplaceRequested { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotationKey = WithLowerCaseTypeCode(Console.ValidateAnnotationKey(AnnotationKey));
        var fullKey = Context.GetFullKey(annotationKey);

        if (RestoreDeleted && Recreate)
        {
            Console.WriteImportant("Use only one of --restore-deleted and --recreate.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var annotation = new _42.Platform.Storyteller.Annotation
        {
            AnnotationKey = annotationKey,
            AnnotationType = annotationKey.Type,
            Name = annotationKey.Name,
            ProjectName = fullKey.ProjectName,
            ViewName = fullKey.ViewName,
        };

        if (IsImportRequested)
        {
            if (!_fileSystem.File.Exists(ImportFilePath))
            {
                Console.WriteImportant($"The import file '{_fileSystem.Path.GetFullPath(ImportFilePath!)}' does not exist.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            object? serializedAnnotation;

            try
            {
                using var fileReader = _fileSystem.File.OpenText(ImportFilePath);
                // A date-like string stays as written: parsed, it would be written back in the offset of this machine.
                await using var jsonReader = new JsonTextReader(fileReader) { DateParseHandling = DateParseHandling.None };
                var fileContent = await JObject.LoadAsync(
                    jsonReader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Ignore,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    });

                var annotationType = AnnotationTypes.GetRuntimeType(annotationKey.Type);
                serializedAnnotation = fileContent.ToObject(annotationType, JsonSerializer.CreateDefault());
            }
            catch (JsonException exception)
            {
                // a file that is not JSON, a JSON array, or a property of the wrong type: the input is wrong, not the tool
                Console.WriteErrorLine($"The import file '{_fileSystem.Path.GetFileName(ImportFilePath)}' does not contain a valid annotation: {exception.Message}");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            if (serializedAnnotation is not _42.Platform.Storyteller.Annotation castedAnnotation)
            {
                Console.WriteImportant($"The import file '{_fileSystem.Path.GetFileName(ImportFilePath)}' does not contain a valid annotation.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            annotation = castedAnnotation;
        }

        annotation = annotation with
        {
            AnnotationKey = annotationKey,
            AnnotationType = annotationKey.Type,
            Name = annotationKey.Name,
            ProjectName = fullKey.ProjectName,
            ViewName = fullKey.ViewName,
        };

        if (AreCustomPropertiesSpecified)
        {
            var customProperties = new Dictionary<string, object>();

            foreach (var property in CustomProperties ?? Array.Empty<string>())
            {
                var parts = property.Split('=', 2);

                if (parts.Length != 2)
                {
                    Console.WriteImportant($"The inline property '{property}' is not in the correct format.");
                    return ExitCodes.ERROR_WRONG_INPUT;
                }

                customProperties[parts[0]] = PropertyValueParser.ParseValue(parts[1]);
            }

            // An option wins over the same name in the file.
            var values = annotation.Values?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];

            foreach (var (name, value) in customProperties)
            {
                values[name] = value;
            }

            annotation = annotation with
            {
                Values = values,
            };
        }

        if (AreLabelsSpecified)
        {
            // The options in their order, then the new labels of the file.
            annotation = annotation with
            {
                Labels = (Labels ?? []).Concat(annotation.Labels ?? []).Distinct(StringComparer.Ordinal).ToList(),
            };
        }

        try
        {
            // the lookup and the restore of deleted keys fail like the write: with the server's message
            var (exitCode, createNew) = await HandleDeletedKeysAsync(annotationKey);

            if (exitCode.HasValue)
            {
                return exitCode.Value;
            }

            return IsReplaceRequested
                ? await ReplaceAsync(annotationKey, annotation, createNew)
                : await MergeAsync(annotationKey, annotation, createNew);
        }
        catch (JsonException e)
        {
            // The client refused to write the body, before any request: a defect of sform, not of the input. The
            // message names the member and its path, not a value.
            Console.WriteErrorLine($"The annotation {annotationKey} could not be sent: {e.Message}");
            return ExitCodes.ERROR_CRASH;
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            // the server's message and code: for example InvalidAnnotation (400), where the server names the field and its
            // limit, a 409 (a concurrent change, a deleted annotation) or a 412 (a replace of a version that changed since
            // its read, AnnotationPreconditionFailed), all 2; the restore of --restore-deleted by a machine without the
            // management scope Annotation.Manage (403, 44); a failure of the service 3
            return Console.WriteApiError(e);
        }
    }

    // The API stores an annotation under its key as written, so a type code in another case would be a second annotation
    // next to the real one.
    private static _42.Platform.Storyteller.AnnotationKey WithLowerCaseTypeCode(_42.Platform.Storyteller.AnnotationKey key)
    {
        var text = key.ToString();
        return _42.Platform.Storyteller.AnnotationKey.Parse(key.TypeCode.ToLowerInvariant() + text[key.TypeCode.Length..]);
    }

    // A patch that found no key, which the merge creates: a missing one, or a deleted one when the user chose Create new.
    private static bool IsToCreate(ApiException exception, bool createNew)
    {
        var errorCode = exception.GetErrorCode();
        return errorCode == AnnotationNotFound || (createNew && errorCode == AnnotationDeleted);
    }

    // One patch, merged on the server into the current state, without a read first. A missing key is created with the
    // input alone, only while it is missing (If-None-Match: *); a key created meanwhile is patched in the next round.
    // Create new starts with the create, which re-creates the deleted keys. A key deleted after the lookup is not
    // re-created silently: a run again asks what happens to it.
    private async Task<int> MergeAsync(_42.Platform.Storyteller.AnnotationKey annotationKey, _42.Platform.Storyteller.Annotation annotation, bool createNew)
    {
        var patch = annotation.ToJsonPatch().ToJson();

        for (var round = 1; round <= MaxWriteRounds; round++)
        {
            if (round > 1 || !createNew)
            {
                try
                {
                    var patched = await _annotationsApi.PatchAnnotationVersionedAsync(
                        Context.OrganizationName,
                        Context.ProjectName,
                        Context.ViewName,
                        annotationKey,
                        patch);

                    Console.WriteJson(patched.Result);
                    return ExitCodes.SUCCESS;
                }
                catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound && IsToCreate(e, createNew))
                {
                    // created below
                }
                catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound && e.GetErrorCode() == AnnotationDeleted)
                {
                    Console.WriteLine($"The annotation {annotationKey} was deleted meanwhile; run the command again to choose what happens to it.");
                    return ExitCodes.ERROR_CRASH;
                }
            }

            try
            {
                var created = await _annotationsApi.SetAnnotationAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    annotationKey,
                    annotation.ToSdkAnnotation(),
                    if_None_Match: AnyVersion);

                Console.WriteJson(created);
                return ExitCodes.SUCCESS;
            }
            catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.PreconditionFailed)
            {
                // created or restored meanwhile: patched in the next round
            }
        }

        Console.WriteLine($"The annotation {annotationKey} changed concurrently; try again.");
        return ExitCodes.ERROR_CRASH;
    }

    // The input replaces the content of the version read, whatever its status, so a change after the read answers 412
    // instead of being overwritten. A missing key is only created, so one created meanwhile is not replaced. A key, or an
    // ancestor, deleted after the lookup is not re-created unless the user chose Create new: a run again asks.
    private async Task<int> ReplaceAsync(_42.Platform.Storyteller.AnnotationKey annotationKey, _42.Platform.Storyteller.Annotation annotation, bool createNew)
    {
        string? ifMatch = null;
        string? ifNoneMatch = null;

        try
        {
            var stored = await _annotationsApi.GetAnnotationVersionedAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                annotationKey,
                includeInactive: true);

            // without a tag the write could not be of the version read: nothing is written
            if (string.IsNullOrEmpty(stored.ETag))
            {
                Console.WriteLine($"The API answered the annotation {annotationKey} without an ETag, so --replace cannot write only the version it read; nothing was written.");
                return ExitCodes.ERROR_SERVICE_FAILURE;
            }

            ifMatch = stored.ETag;
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // the read does not tell a missing key from a deleted one, the lookup does
            if (!createNew && (await FindDeletedAsync(annotationKey)) is { Count: > 0 })
            {
                Console.WriteLine($"The annotation {annotationKey} or an ancestor was deleted meanwhile; run the command again to choose what happens to it.");
                return ExitCodes.ERROR_CRASH;
            }

            ifNoneMatch = AnyVersion;
        }

        var written = await _annotationsApi.SetAnnotationAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            annotationKey,
            annotation.ToSdkAnnotation(),
            ifMatch,
            ifNoneMatch);

        Console.WriteJson(written);
        return ExitCodes.SUCCESS;
    }

    // The deleted annotations among the key and its ancestors.
    private async Task<ICollection<_42.Platform.Storyteller.Sdk.Annotation>> FindDeletedAsync(_42.Platform.Storyteller.AnnotationKey annotationKey)
    {
        var keys = annotationKey.GetAllAncestorKeys()
            .Append(annotationKey)
            .Select(key => key.ToString())
            .ToList();

        return await _annotationsApi.FindDeletedAnnotationsAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            new _42.Platform.Storyteller.Sdk.AnnotationKeysRequest { Keys = keys });
    }

    // A write to a deleted key, or below a deleted ancestor, re-creates it empty. Before that happens, the user chooses:
    // restore the deleted keys and update them, or create them new. Returns an exit code when the write must not go on,
    // and whether the write creates the deleted keys new.
    private async Task<(int? ExitCode, bool CreateNew)> HandleDeletedKeysAsync(_42.Platform.Storyteller.AnnotationKey annotationKey)
    {
        var deleted = await FindDeletedAsync(annotationKey);

        if (deleted is not { Count: > 0 })
        {
            return (null, false);
        }

        Console.WriteLine(deleted.Count == 1 ? "This annotation is deleted:" : "These annotations are deleted:");

        foreach (var annotation in deleted)
        {
            Console.WriteLine(annotation.Deletion is { } deletion
                ? $"- {annotation.AnnotationKey} ({AuditConsoleExtensions.FormatDeletion(deletion)})"
                : $"- {annotation.AnnotationKey}");
        }

        string choice;

        if (RestoreDeleted)
        {
            choice = RestoreAndUpdateChoice;
        }
        else if (Recreate)
        {
            choice = CreateNewChoice;
        }
        else
        {
            Console.EnsureInteractiveValue(
                "what happens to the deleted annotations",
                "Choose with an option: --restore-deleted restores and updates them, --recreate creates them new.");

            choice = Console.Select(new SelectOptions<string>
            {
                Message = "What do you want to do",
                Items = new[] { RestoreAndUpdateChoice, CreateNewChoice, CancelChoice },
                DefaultValue = RestoreAndUpdateChoice,
            });
        }

        if (choice == CancelChoice)
        {
            Console.WriteLine("Nothing was changed.");
            return (ExitCodes.WARNING_ABORTED, false);
        }

        if (choice == CreateNewChoice)
        {
            return (null, true);
        }

        var result = await _annotationsApi.RestoreAnnotationsAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            new _42.Platform.Storyteller.Sdk.AnnotationRestoreRequest
            {
                Keys = deleted.Select(annotation => annotation.AnnotationKey).ToList(),
                Descendants = false,
            });

        if (result.Failed is { Count: > 0 })
        {
            foreach (var failure in result.Failed)
            {
                Console.WriteLine($"The annotation '{failure.Key}' was not restored: {failure.Message}");
            }

            return (ExitCodes.ERROR_WRONG_INPUT, false);
        }

        Console.WriteLine($"Restored {string.Join(", ", (result.Annotations ?? []).Select(annotation => annotation.AnnotationKey))}.");
        return (null, false);
    }
}
