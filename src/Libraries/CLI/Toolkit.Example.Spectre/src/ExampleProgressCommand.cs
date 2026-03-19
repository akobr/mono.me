using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit.Example;

[Command("progress", Description = "Examples of progress bars.")]
public class ExampleProgressCommand : BaseCommand
{
    public ExampleProgressCommand(IExtendedConsole console)
        : base(console)
    {
        // no operation
    }

    public override async Task<int> OnExecuteAsync()
    {
        using var main = Console.StartProgressBar("your game is almost ready");
        using var downloading = Console.StartProgressBar("downloading");
        using var installing = Console.StartProgressBar("installing");

        for (var i = 0; i <= 10; i++)
        {
            downloading.Tick(i * 10, i < 10 ? "downloading" : "download complete");
            installing.Tick(i * 10, i < 10 ? "installing" : "install complete");
            main.Tick(i * 10, i < 10 ? "your game is almost ready" : "your game is ready!");
            await Task.Delay(500);
        }

        return 0;
    }
}
