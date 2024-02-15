using System.Net;
using System.Threading.Tasks;
using _42.Platform.Sdk.Api;
using _42.Platform.Storyteller;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete a configuration.")]
public class ConfigDeleteCommand : BaseContextCommand
{
    private readonly IConfigurationApiAsync _configurationApi;

    public ConfigDeleteCommand(
        IExtendedConsole console,
        IConfigurationApiAsync configurationApi,
        ICommandContext context)
        : base(console, context)
    {
        _configurationApi = configurationApi;
    }

    [Argument(0, Description = "An annotation key to delete the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var response = await _configurationApi.DeleteConfigurationWithHttpInfoAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            AnnotationKey);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The configuration for '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteLine($"The configuration for '{AnnotationKey}' has been deleted.");
        return ExitCodes.SUCCESS;

    }
}
