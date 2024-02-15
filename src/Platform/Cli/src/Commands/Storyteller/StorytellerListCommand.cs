using System.Threading.Tasks;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands.Storyteller;

[Subcommand(
    typeof(StorytellerGetCommand),
    typeof(StorytellerSetCommand),
    typeof(StorytellerDeleteCommand))]

[Command(CommandNames.STORYTELLER, CommandNames.STORY, CommandNames.ANNOTATIONS, Description = "Retrieve the story of your platform.")]
public class StorytellerListCommand : BaseContextCommand
{
    private readonly IAnnotationsApiAsync _annotationsApi;

    public StorytellerListCommand(
        IExtendedConsole console,
        IAnnotationsApiAsync annotationsApi,
        ICommandContext context)
        : base(console, context)
    {
        _annotationsApi = annotationsApi;
    }

    protected override async Task<int> ExecuteAsync()
    {
        var annotations = await _annotationsApi.GetAnnotationsAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName);
        return ExitCodes.SUCCESS;
    }
}
