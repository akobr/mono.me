using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;
using IExtendedConsole = _42.CLI.Toolkit.Output.IExtendedConsole;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.LIST, Description = "Retrieve the list of queried annotations.")]
public class StorytellerListCommand : BaseCommand
{
    public StorytellerListCommand(IExtendedConsole console)
        : base(console)
    {
    }

    public override Task<int> OnExecuteAsync()
    {
        throw new System.NotImplementedException();
    }
}
