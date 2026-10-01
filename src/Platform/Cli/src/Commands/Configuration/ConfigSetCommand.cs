using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or update a configuration (merged into the stored content, or replacing it with --replace).")]
public class ConfigSetCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IFileSystem _fileSystem;

    public ConfigSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation key to set the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-r|--resolved", CommandOptionType.NoValue, Description = "Retrieve resolved configuration, corresponding permission is needed.")]
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

    [Option("--replace", CommandOptionType.NoValue, Description = "Replace the stored content of the configuration instead of merging into it (removes properties which are not specified).")]
    public bool IsReplaceRequested { get; set; }

    [Option("-f|--force", CommandOptionType.NoValue, Description = "Store the configuration even when it violates the schema.")]
    public bool Force { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var config = await JsonInputBuilder.BuildAsync(Console, _fileSystem, ImportFilePath, InlineProperties);

        // TODO: [P1] add support for custom properties and labels
        _42.Platform.Storyteller.Sdk.Configuration? data;

        try
        {
            if (IsReplaceRequested)
            {
                var storedContent = await _configurationApi.GetStoredConfigurationContentAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey);

                data = await _configurationApi.ReplaceConfigurationAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey,
                    storedContent,
                    config,
                    Force);

                if (data is null)
                {
                    Console.WriteLine("No changes detected.");
                    return ExitCodes.WARNING_NO_WORK_NEEDED;
                }
            }
            else
            {
                data = await _configurationApi.SetConfigurationAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey,
                    Force,
                    config);
            }

            if (IsResolvedRetrievalRequested)
            {
                data = await _configurationApi.GetResolvedConfigurationAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey);
            }
        }
        catch (ApiException e)
        {
            if (SchemaValidationConsole.TryWrite(Console, e))
            {
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            Console.WriteLine($"Error occurred: {e.Message}");
            return ExitCodes.ERROR_CRASH;
        }

        Console.WriteJson(data);
        return ExitCodes.SUCCESS;
    }
}
