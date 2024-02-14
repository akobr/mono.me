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

    public override Task<int> OnExecuteAsync()
    {
        throw new System.NotImplementedException();
    }
}
