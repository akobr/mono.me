using System.Threading.Tasks;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands.Configuration;

[Subcommand(
    typeof(ConfigSetCommand),
    typeof(ConfigDeleteCommand))]

[Command(CommandNames.CONFIG, CommandNames.CONFIGURATION, Description = "Retrieve a configuration for the annotation.")]
public class ConfigGetCommand : BaseContextCommand
{
    private readonly IConfigurationApiAsync _configurationApi;

    public ConfigGetCommand(
        IExtendedConsole console,
        IConfigurationApiAsync configurationApi,
        ICommandContext context)
        : base(console, context)
    {
        _configurationApi = configurationApi;
    }

    [Argument(0, Description = "An annotation key to get the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Specify a file where the retrieved configuration will be saved.")]
    public string ExportFilePath { get; set; }

    [Option("-r|--resolved", CommandOptionType.NoValue, Description = "Retrieve resolved configuration, administrator permission is needed.")]
    public bool RetrieveResolved { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var configuration = await _configurationApi.GetConfigurationAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            AnnotationKey);

        // TODO: [P1] print configuration in console or file
        return ExitCodes.SUCCESS;

    }
}
