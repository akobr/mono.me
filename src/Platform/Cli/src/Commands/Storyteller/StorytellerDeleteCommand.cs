using System.Net;
using System.Threading.Tasks;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete an annotation.")]
public class StorytellerDeleteCommand : BaseContextCommand
{
    private readonly IAnnotationsApiAsync _annotationsApi;

    public StorytellerDeleteCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiAsync annotationsApi)
        : base(console, context)
    {
        _annotationsApi = annotationsApi;
    }

    [Argument(0, Description = "An annotation key to delete.")]
    public string AnnotationKey { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var response = await _annotationsApi.DeleteAnnotationWithHttpInfoAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            AnnotationKey);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The annotation '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteLine($"The annotation '{AnnotationKey}' has been deleted.");
        return ExitCodes.SUCCESS;
    }
}
