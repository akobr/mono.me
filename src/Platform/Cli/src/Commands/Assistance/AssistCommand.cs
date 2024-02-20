using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Assistance;

[Subcommand(
    typeof(AssistKeyCommand))]

[Command(CommandNames.ASSIST, CommandNames.ASSISTANCE, Description = "Get assistance with the usage of 2S platform.")]
public class AssistCommand : BaseParentCommand
{
    public AssistCommand(IExtendedConsole console, CommandLineApplication application)
        : base(console, application)
    {
        // no operation
    }
}
