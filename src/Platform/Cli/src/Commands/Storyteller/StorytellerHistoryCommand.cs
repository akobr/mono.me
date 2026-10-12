using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.HISTORY, Description = "Show who changed an annotation, when, and what.")]
public class StorytellerHistoryCommand : BaseContextCommand
{
    private readonly IAnnotationsApiClient _annotationsApi;

    public StorytellerHistoryCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiClient annotationsApi)
        : base(console, context)
    {
        _annotationsApi = annotationsApi;
    }

    [Argument(0, Description = "An annotation key, active or deleted.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-n|--top", CommandOptionType.SingleValue, Description = "Show only the newest N entries.")]
    public int? Top { get; set; }

    [Option("--json", CommandOptionType.NoValue, Description = "Write the history as JSON.")]
    public bool Json { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        AnnotationHistory history;

        try
        {
            history = await _annotationsApi.GetAnnotationHistoryAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                annotationKey);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The annotation '{annotationKey}' has no history: it doesn't exist or was deleted permanently.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (Json)
        {
            if (Top is > 0)
            {
                history.Entries = history.Entries?.OrderByDescending(entry => entry.At).Take(Top.Value).ToList();
            }

            Console.WriteJson(history);
            return ExitCodes.SUCCESS;
        }

        Console.WriteHeader($"History of {annotationKey}");
        Console.WriteHistory(history, Top);
        return ExitCodes.SUCCESS;
    }
}
