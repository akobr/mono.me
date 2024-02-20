using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Model;
using _42.Platform.Cli.Output;
using _42.Platform.Sdk.Api;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Entities;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or update an annotation.")]
public class StorytellerSetCommand : BaseContextCommand
{
    private readonly AccessDefaultOptions _accessDefault;
    private readonly IAnnotationsApiAsync _annotationsApi;
    private readonly IFileSystem _fileSystem;

    public StorytellerSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiAsync annotationsApi,
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

    protected override async Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        var fullKey = Context.GetFullKey(annotationKey);

        var annotation = new Annotation
        {
            PartitionKey = fullKey.GetPartitionKey(),
            AnnotationKey = annotationKey,
            AnnotationType = annotationKey.Type,
            Name = annotationKey.Name,
            ProjectName = fullKey.ProjectName,
            ViewName = fullKey.ViewName,
            SystemId = Guid.NewGuid(),
            SystemPublicKey = Guid.NewGuid().ToString("N"),
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

            var annotationType = AnnotationTypeCodes.GetRealType(annotationKey.Type);
            var serializedAnnotation = fileContent.ToObject(annotationType, JsonSerializer.CreateDefault());

            if (serializedAnnotation is not Annotation castedAnnotation)
            {
                Console.WriteImportant($"The import file '{_fileSystem.Path.GetFileName(ImportFilePath)}' does not contain a valid annotation.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            annotation = castedAnnotation;
        }

        annotation = annotation with
        {
            PartitionKey = fullKey.GetPartitionKey(),
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

                // TODO: [P2] try to parse the value to a proper type (double, int, bool)
                customProperties[parts[0]] = parts[1];
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

            if (annotation.Labels is null)
            {
                annotation = annotation with
                {
                    Labels = labels,
                };
            }
            else
            {
                annotation = annotation with
                {
                    Labels = new HashSet<string>(annotation.Labels.Concat(labels)),
                };
            }
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
}
