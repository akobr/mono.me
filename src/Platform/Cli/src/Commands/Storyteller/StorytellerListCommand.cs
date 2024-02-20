using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Storyteller;

[Subcommand(
    typeof(StorytellerGetCommand),
    typeof(StorytellerSetCommand),
    typeof(StorytellerDeleteCommand),
    typeof(ConfigGetCommand))]

[Command(CommandNames.STORYTELLER, CommandNames.STORY, CommandNames.ANNOTATIONS, Description = "Retrieve the story of your platform (manage annotations).")]
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

    [Option("-r|--responsibilities", CommandOptionType.SingleValue, Description = "Query responsibilities by name.")]
    public string? QueryResponsibilities { get; set; }

    [Option("-s|--subjects", CommandOptionType.SingleValue, Description = "Query subjects by name.")]
    public string? QuerySubjects { get; set; }

    [Option("-u|--usages", CommandOptionType.NoValue, Description = "Query usages, can be combined with responsibilities or subjects.")]
    public bool QueryUsages { get; set; }

    [Option("-c|--contexts", CommandOptionType.SingleValue, Description = "Query contexts by name.")]
    public string? QueryContexts { get; set; }

    [Option("-e|--executions", CommandOptionType.NoValue, Description = "Query executions, can be combined with responsibilities, subjects, or contexts.")]
    public bool QueryExecutions { get; set; }

    [Option("-d|--descendants", CommandOptionType.SingleValue, Description = "Retrieve descendants of a specific annotation.")]
    public string? QueryDescendants { get; set; }

    [Option("-t|--tree", CommandOptionType.NoValue, Description = "Show results as tree based by subjects.")]
    public bool ShowAsTree { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotations = await _annotationsApi.GetAnnotationsAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName);
        return ExitCodes.SUCCESS;
    }
}
