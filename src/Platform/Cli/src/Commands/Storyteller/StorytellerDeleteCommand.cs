using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using ApiSdk;
using ApiSdk.Models;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete an annotation and its descendants.")]
public class StorytellerDeleteCommand : BaseContextCommand
{
    private readonly ApiClient _apiClient;

    public StorytellerDeleteCommand(
        IExtendedConsole console,
        ICommandContext context,
        ApiClient apiClient)
        : base(console, context)
    {
        _apiClient = apiClient;
    }

    [Argument(0, Description = "An annotation key to delete.")]
    public string AnnotationKey { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(AnnotationKey))
        {
            Console.WriteImportant("Please specify annotation key.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        try
        {
            await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Annotations[AnnotationKey]
                .DeleteAsync();
        }
        catch (ErrorResponse)
        {
            Console.WriteLine($"The annotation '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteImportant($"The annotation '{AnnotationKey}' has been deleted.");
        return ExitCodes.SUCCESS;
    }
}
