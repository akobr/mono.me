using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete an annotation.")]
public class StorytellerDeleteCommand : BaseCommand
{
    public StorytellerDeleteCommand(IExtendedConsole console)
        : base(console)
    {
    }

    public override Task<int> OnExecuteAsync()
    {
        throw new System.NotImplementedException();
    }
}
