using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or update a configuration.")]
public class ConfigSetCommand : BaseContextCommand
{
    private static readonly JsonMergeSettings JsonMergeOptions = new()
    {
        MergeArrayHandling = MergeArrayHandling.Union,
        MergeNullValueHandling = MergeNullValueHandling.Ignore,
        PropertyNameComparison = StringComparison.Ordinal,
    };

    private readonly IConfigurationApiAsync _configurationApi;
    private readonly IFileSystem _fileSystem;

    public ConfigSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationApiAsync configurationApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation key to set the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-r|--resolved", CommandOptionType.NoValue, Description = "Retrieve resolved configuration, administrator permission is needed.")]
    public bool IsResolvedRetrievalRequested { get; set; }

    [Option("-i|--import", CommandOptionType.SingleValue, Description = "Specify a file from where the configuration(s) will be imported.")]
    public string? ImportFilePath { get; set; }

    public bool IsImportRequested => !string.IsNullOrWhiteSpace(ImportFilePath);

    [Option("-x|--properties", CommandOptionType.MultipleValue, Description = "Specify inline properties to be set on the configuration.")]
    public string[]? InlineProperties { get; set; }

    public bool AreInlinePropertiesSpecified => InlineProperties?.Length > 0;

    [Option("-c|--custom-properties", CommandOptionType.MultipleValue, Description = "Specify custom properties to be set on the configuration.")]
    public string[]? CustomProperties { get; set; }

    public bool AreCustomPropertiesSpecified => CustomProperties?.Length > 0;

    [Option("-l|--labels", CommandOptionType.MultipleValue, Description = "Specify labels to be set on the configuration.")]
    public string[]? Labels { get; set; }

    public bool AreLabelsSpecified => Labels?.Length > 0;

    protected override async Task<int> ExecuteAsync()
    {
        var config = new JObject();

        if (IsImportRequested)
        {
            if (!_fileSystem.File.Exists(ImportFilePath))
            {
                Console.WriteImportant($"The file '{_fileSystem.Path.GetFullPath(ImportFilePath!)}' does not exist.");
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

            config.Merge(fileContent, JsonMergeOptions);
        }

        if (AreInlinePropertiesSpecified)
        {
            JObject inlineContent = new();

            foreach (var inlineProperty in InlineProperties ?? Array.Empty<string>())
            {
                var parts = inlineProperty.Split('=', 2);

                if (parts.Length != 2)
                {
                    Console.WriteImportant($"The inline property '{inlineProperty}' is not in the correct format.");
                    return ExitCodes.ERROR_WRONG_INPUT;
                }

                inlineContent[parts[0]] = parts[1];
            }

            config.Merge(inlineContent, JsonMergeOptions);
        }

        // TODO: [P1] add support for custom properties and labels
        var response = await _configurationApi.SetConfigurationWithHttpInfoAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            AnnotationKey,
            config);

        if (IsResolvedRetrievalRequested)
        {
            response = await _configurationApi.GetResolvedConfigurationWithHttpInfoAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                AnnotationKey);
        }

        Console.WriteJson(response.Data);
        return ExitCodes.SUCCESS;

    }
}
