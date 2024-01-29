using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands;

[Command(CommandNames.STORYTELLER, CommandNames.STORY, Description = "Retrieve and manipulate with the story of your platform.")]
public class StorytellerCommand : BaseCommand
{
    public StorytellerCommand(IExtendedConsole console)
        : base(console)
    {
    }

    public override Task<int> OnExecuteAsync()
    {
        throw new System.NotImplementedException();
    }
}
