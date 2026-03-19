using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit;

public abstract class BaseParentCommand : BaseCommand
{
    private readonly CommandLineApplication _application;

    protected BaseParentCommand(IExtendedConsole console, CommandLineApplication application)
        : base(console)
    {
        _application = application;
    }

    public override Task<int> OnExecuteAsync()
    {
        return SelectSubCommandAndExecuteAsync(_application, Console);
    }

    internal static Task<int> SelectSubCommandAndExecuteAsync(CommandLineApplication application, IExtendedConsole console)
    {
        var choices = application.Commands
            .Where(c => !string.IsNullOrEmpty(c.Name))
            .Select(c => c.Name!);

        var commandName = console.Select(
            $"Which {application.Name} command you want to execute",
            choices);

        return application.Commands.ExecuteByNameAsync(commandName);
    }
}
