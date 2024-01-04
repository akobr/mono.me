using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;

namespace _42.CLI.Toolkit;

public abstract class BaseCommand : IAsyncCommand
{
    protected BaseCommand(IExtendedConsole console)
    {
        Console = console;
    }

    protected IExtendedConsole Console { get; }

    public abstract Task<int> OnExecuteAsync();
}
