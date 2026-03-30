using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using ApiSdk;
using ApiSdk.Models;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete a configuration.")]
public class ConfigDeleteCommand : BaseContextCommand
{
    private readonly ApiClient _apiClient;

    public ConfigDeleteCommand(
        IExtendedConsole console,
        ApiClient apiClient,
        ICommandContext context)
        : base(console, context)
    {
        _apiClient = apiClient;
    }

    [Argument(0, Description = "An annotation key to delete the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        try
        {
            await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Configuration[AnnotationKey]
                .DeleteAsync();
        }
        catch (ErrorResponse)
        {
            Console.WriteLine($"The configuration for '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteImportant($"The configuration for '{AnnotationKey}' has been deleted.");
        return ExitCodes.SUCCESS;
    }
}
