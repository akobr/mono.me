using System.Threading.Tasks;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or update a configuration.")]
public class ConfigSetCommand : BaseContextCommand
{
    private readonly IConfigurationApiAsync _configurationApi;

    public ConfigSetCommand(
        IExtendedConsole console,
        IConfigurationApiAsync configurationApi,
        ICommandContext context)
        : base(console, context)
    {
        _configurationApi = configurationApi;
    }

    [Argument(0, Description = "An annotation key to set the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-r|--resolved", CommandOptionType.NoValue, Description = "Retrieve resolved configuration, administrator permission is needed.")]
    public bool RetrieveResolved { get; set; }

    [Option("-i|--import", CommandOptionType.SingleValue, Description = "Specify a file from where the configuration(s) will be imported.")]
    public string? ImportFilePath { get; set; }

    [Option("-r|--properties", CommandOptionType.MultipleValue, Description = "Specify inline properties to be set on the configuration.")]
    public string[]? InlineProperties { get; set; }

    [Option("-c|--custom-properties", CommandOptionType.MultipleValue, Description = "Specify custom properties to be set on the configuration.")]
    public string[]? CustomProperties { get; set; }

    [Option("-l|--labels", CommandOptionType.MultipleValue, Description = "Specify labels to be set on the configuration.")]
    public string[]? Labels { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        // TODO: [P1] build and save new configuration
        var configuration = await _configurationApi.GetConfigurationAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            AnnotationKey);


        // TODO: [P1] print new configuration in console
        return ExitCodes.SUCCESS;

    }
}
