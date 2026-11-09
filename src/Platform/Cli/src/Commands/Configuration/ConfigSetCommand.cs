using System.IO.Abstractions;
using System.Net;
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

    [Option("-x|--properties", CommandOptionType.MultipleValue, Description = "Specify inline properties as path=value, for example -x \"db.port=5432\" -x \"hosts[0]=a\" -x \"Logging.LogLevel['Microsoft.AspNetCore']=Warning\"; quote each one. Without --replace, the API appends an indexed item to a stored array.")]
    public string[]? InlineProperties { get; set; }

    public bool AreInlinePropertiesSpecified => InlineProperties?.Length > 0;

    // No -c|--custom-properties or -l|--labels: the write of a configuration takes its content alone, and the API stores
    // no custom properties or labels of a configuration (they belong to the annotation, see story set).
    [Option("--replace", CommandOptionType.NoValue, Description = "Replace the stored content of the configuration instead of merging into it (removes properties which are not specified).")]
    public bool IsReplaceRequested { get; set; }

    [Option("-f|--force", CommandOptionType.NoValue, Description = "Store the configuration even when it violates the schema.")]
    public bool Force { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var config = await JsonInputBuilder.BuildAsync(Console, _fileSystem, ImportFilePath, InlineProperties);
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
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            // a schema violation lists the configurations; a 403 (a Reader, a machine without Configuration.Write) is 44
            // with the server's message, a 412 or another rejection 2, a failure of the service 3
            return SchemaValidationConsole.WriteWriteError(Console, e);
        }

        Console.WriteJson(data);
        return ExitCodes.SUCCESS;
    }
}
