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
using _42.Platform.Storyteller.Sdk;
using _42.Platform.Storyteller;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or update an annotation.")]
public class StorytellerSetCommand : BaseContextCommand
{
    internal const string RestoreAndUpdateChoice = "Restore and update (keeps the configuration and the history)";
    internal const string CreateNewChoice = "Create new (starts with an empty configuration)";
    internal const string CancelChoice = "Cancel";

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

    [Argument(0, Description = "An annotation key to get.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-i|--import", CommandOptionType.SingleValue, Description = "Specify a file from where the annotation(s) will be imported.")]
    public string? ImportFilePath { get; set; }

    public bool IsImportRequested => !string.IsNullOrWhiteSpace(ImportFilePath);

    [Option("-c|--custom-properties", CommandOptionType.MultipleValue, Description = "Specify custom properties to be set on the configuration.")]
    public string[]? CustomProperties { get; set; }

    public bool AreCustomPropertiesSpecified => CustomProperties?.Length > 0;

    [Option("-l|--labels", CommandOptionType.MultipleValue, Description = "Specify labels to be set on the configuration.")]
    public string[]? Labels { get; set; }

    public bool AreLabelsSpecified => Labels?.Length > 0;

    [Option("--restore-deleted", CommandOptionType.NoValue, Description = "When the annotation or an ancestor is deleted, restore it (with its configuration and history) and update it.")]
    public bool RestoreDeleted { get; set; }

    [Option("--recreate", CommandOptionType.NoValue, Description = "When the annotation or an ancestor is deleted, create it new, with an empty configuration.")]
    public bool Recreate { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
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

            using var fileReader = _fileSystem.File.OpenText(ImportFilePath);
            await using var jsonReader = new JsonTextReader(fileReader);
            var fileContent = await JObject.LoadAsync(
                jsonReader,
                new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Ignore,
                });

            var annotationType = AnnotationTypes.GetRuntimeType(annotationKey.Type);
            var serializedAnnotation = fileContent.ToObject(annotationType, JsonSerializer.CreateDefault());

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

            if (annotation.Values is null)
            {
                annotation = annotation with
                {
                    Values = customProperties,
                };
            }
            else
            {
                annotation = annotation with
                {
                    Values = new Dictionary<string, object>(annotation.Values.Concat(customProperties)),
                };
            }
        }

        if (AreLabelsSpecified)
        {
            var labels = new HashSet<string>();

            foreach (var label in Labels ?? Array.Empty<string>())
            {
                labels.Add(label);
            }

            if (annotation.Labels is not null)
            {
                labels.UnionWith(annotation.Labels);
            }

            annotation = annotation with
            {
                Labels = labels.ToList(),
            };
        }

        try
        {
            // the lookup and the restore of deleted keys fail like the write: with the server's message
            var deletedResult = await HandleDeletedKeysAsync(annotationKey);

            if (deletedResult.HasValue)
            {
                return deletedResult.Value;
            }

            var resultAnnotation = await _annotationsApi.SetAnnotationAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                annotationKey,
                annotation.ToSdkAnnotation());

            Console.WriteJson(resultAnnotation);
            return ExitCodes.SUCCESS;
        }
        catch (ApiException e) when (e.StatusCode is (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.Conflict)
        {
            // for example InvalidAnnotation: the server names the field and its limit
            Console.WriteLine(e.GetErrorMessage());
            return e.StatusCode == (int)HttpStatusCode.BadRequest ? ExitCodes.ERROR_WRONG_INPUT : ExitCodes.ERROR_CRASH;
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            Console.WriteLine($"Error occurred: {e.Message}");
            return ExitCodes.ERROR_CRASH;
        }
    }

    // A write to a deleted key, or below a deleted ancestor, re-creates it empty. Before that happens, the user chooses:
    // restore the deleted keys and update them, or create them new. Returns an exit code when the write must not go on.
    private async Task<int?> HandleDeletedKeysAsync(_42.Platform.Storyteller.AnnotationKey annotationKey)
    {
        var keys = annotationKey.GetAllAncestorKeys()
            .Append(annotationKey)
            .Select(key => key.ToString())
            .ToList();

        var deleted = await _annotationsApi.FindDeletedAnnotationsAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            new _42.Platform.Storyteller.Sdk.AnnotationKeysRequest { Keys = keys });

        if (deleted is not { Count: > 0 })
        {
            return null;
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
        else if (Console.Console?.IsInputRedirected == true)
        {
            Console.WriteLine("Choose what happens to them: --restore-deleted restores and updates them, --recreate creates them new.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        else
        {
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
            return ExitCodes.WARNING_ABORTED;
        }

        if (choice == CreateNewChoice)
        {
            return null;
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

            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteLine($"Restored {string.Join(", ", (result.Annotations ?? []).Select(annotation => annotation.AnnotationKey))}.");
        return null;
    }
}

