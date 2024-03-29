using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands;

[Command(CommandNames.SUPERVISOR, CommandNames.SUPERVISE, Description = "Supervise and over-watch your platform.")]
public class SupervisorCommand : BaseCommand
{
    public SupervisorCommand(IExtendedConsole console)
        : base(console)
    {
    }

    public override Task<int> OnExecuteAsync()
    {
        Console.WriteImportant("Under the development, will be implemented in version 1.0");
        return Task.FromResult(ExitCodes.SUCCESS);
    }
}
