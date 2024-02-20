using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;

namespace _42.Platform.Cli.Commands.Configuration;

[Subcommand(
    typeof(ConfigSetCommand),
    typeof(ConfigDeleteCommand))]

[Command(CommandNames.CONFIG, CommandNames.CONFIGURATION, Description = "Get and manage configuration in context of an annotation.")]
public class ConfigGetCommand : BaseContextCommand
{
    private readonly IConfigurationApiAsync _configurationApi;
    private readonly IFileSystem _fileSystem;

    public ConfigGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationApiAsync configurationApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation key to get the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Specify a file where the retrieved configuration will be saved.")]
    public string ExportFilePath { get; set; }

    public bool IsExportRequested => !string.IsNullOrWhiteSpace(ExportFilePath);

    [Option("-r|--resolved", CommandOptionType.NoValue, Description = "Retrieve resolved configuration, administrator permission is needed.")]
    public bool IsResolvedRetrievalRequested { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var response = IsResolvedRetrievalRequested
            ? await _configurationApi.GetResolvedConfigurationWithHttpInfoAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                AnnotationKey)
            : await _configurationApi.GetConfigurationWithHttpInfoAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                AnnotationKey);

        Console.WriteJson(response.Data);

        if (IsExportRequested)
        {
            Console.WriteJsonToFile(response.Data, ExportFilePath, _fileSystem);
        }

        return ExitCodes.SUCCESS;

    }
}
