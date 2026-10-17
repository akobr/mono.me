using System.IO.Abstractions;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;

namespace _42.Platform.Cli.Commands.Configuration;

[Subcommand(
    typeof(ConfigSetCommand),
    typeof(ConfigEditCommand),
    typeof(ConfigPatchCommand),
    typeof(ConfigDeleteCommand),
    typeof(ConfigDiffCommand),
    typeof(ConfigVersionsCommand))]

[Command(CommandNames.CONFIG, CommandNames.CONFIGURATION, Description = "Get and manage configuration in context of an annotation.")]
public class ConfigGetCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IFileSystem _fileSystem;

    public ConfigGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi,
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

    [Option("-r|--resolved", CommandOptionType.NoValue, Description = "Retrieve resolved configuration, corresponding permission is needed.")]
    public bool IsResolvedRetrievalRequested { get; set; }

    [Option("--version", CommandOptionType.SingleValue, Description = "Retrieve the stored content of a specific version (see 'versions').")]
    public int? Version { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        try
        {
            if (Version.HasValue && IsResolvedRetrievalRequested)
            {
                Console.WriteLine("Cannot specify both --version and --resolved.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            var data = Version.HasValue
                ? await _configurationApi.GetConfigurationVersionAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey,
                    Version.Value)
                : IsResolvedRetrievalRequested
                ? await _configurationApi.GetResolvedConfigurationAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey)
                : await _configurationApi.GetConfigurationAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey);

            Console.WriteJson(data);

            if (IsExportRequested)
            {
                Console.WriteJsonToFile(data, ExportFilePath, _fileSystem);
            }
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine(Version.HasValue
                ? $"The version {Version} of the configuration for '{AnnotationKey}' has not been found."
                : $"The configuration for '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        return ExitCodes.SUCCESS;

    }
}

