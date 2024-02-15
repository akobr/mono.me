using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or update an annotation.")]
public class StorytellerSetCommand : BaseCommand
{
    public StorytellerSetCommand(IExtendedConsole console)
        : base(console)
    {
    }

    [Argument(0, Description = "An annotation key to get.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-i|--import", CommandOptionType.SingleValue, Description = "Specify a file from where the annotation(s) will be imported.")]
    public string? ImportFilePath { get; set; }

    [Option("-c|--custom-properties", CommandOptionType.MultipleValue, Description = "Specify custom properties to be set on the configuration.")]
    public string[]? CustomProperties { get; set; }

    [Option("-l|--labels", CommandOptionType.MultipleValue, Description = "Specify labels to be set on the configuration.")]
    public string[]? Labels { get; set; }

    public override Task<int> OnExecuteAsync()
    {
        throw new System.NotImplementedException();
    }
}
