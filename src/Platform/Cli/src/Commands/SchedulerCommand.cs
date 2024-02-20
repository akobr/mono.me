using System;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands;

[Command(CommandNames.SCHEDULER, CommandNames.SCHEDULE, Description = "Manage time based jobs / units.")]
public class SchedulerCommand : BaseCommand
{
    public SchedulerCommand(IExtendedConsole console)
        : base(console)
    {
    }

    public override Task<int> OnExecuteAsync()
    {
        Console.WriteImportant("Under the development, will be implemented in version 1.1");
        return Task.FromResult(ExitCodes.SUCCESS);
    }
}
