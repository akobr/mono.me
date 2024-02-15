using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.GET, Description = "Retrieve a specific annotation.")]
public class StorytellerGetCommand : BaseCommand
{
    public StorytellerGetCommand(IExtendedConsole console)
        : base(console)
    {
    }

    [Argument(0, Description = "An annotation key to get.")]
    public string AnnotationKey { get; set; } = string.Empty;

    public override Task<int> OnExecuteAsync()
    {
        throw new System.NotImplementedException();
    }
}
