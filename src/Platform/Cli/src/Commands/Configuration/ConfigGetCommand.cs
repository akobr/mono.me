using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using ApiSdk;
using ApiSdk.Models;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Configuration;

[Subcommand(
    typeof(ConfigSetCommand),
    typeof(ConfigDeleteCommand))]

[Command(CommandNames.CONFIG, CommandNames.CONFIGURATION, Description = "Get and manage configuration in context of an annotation.")]
public class ConfigGetCommand : BaseContextCommand
{
    private readonly ApiClient _apiClient;
    private readonly IFileSystem _fileSystem;

    public ConfigGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        ApiClient apiClient,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _apiClient = apiClient;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation key to get the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Specify a file where the retrieved configuration will be saved.")]
    public string ExportFilePath { get; set; } = string.Empty;

    public bool IsExportRequested => !string.IsNullOrWhiteSpace(ExportFilePath);

    [Option("-r|--resolved", CommandOptionType.NoValue, Description = "Retrieve resolved configuration, corresponding permission is needed.")]
    public bool IsResolvedRetrievalRequested { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ApiSdk.Models.Configuration? config;

        try
        {
            config = IsResolvedRetrievalRequested
                ? await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Configuration[AnnotationKey].Resolved
                    .GetAsync()
                : await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Configuration[AnnotationKey]
                    .GetAsync();
        }
        catch (ErrorResponse)
        {
            Console.WriteLine($"The configuration for '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteJson(config);

        if (IsExportRequested)
        {
            Console.WriteJsonToFile(config, ExportFilePath, _fileSystem);
        }

        return ExitCodes.SUCCESS;
    }
}
