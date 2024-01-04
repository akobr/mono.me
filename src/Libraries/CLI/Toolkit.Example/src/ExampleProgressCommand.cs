using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;
using ShellProgressBar;

namespace _42.CLI.Toolkit.Example;

[Command("progress", Description = "Examples of a progress bars.")]
public class ExampleProgressCommand : BaseCommand
{
    public ExampleProgressCommand(IExtendedConsole console)
        : base(console)
    {
        // no operation
    }

    public override async Task<int> OnExecuteAsync()
    {
        using var main = (ProgressBar)Console.StartProgressBar("you game is almost ready");
        using var downloading = (ChildProgressBar)Console.StartProgressBar(
            "downloading",
            new ProgressBarOptions
            {
                ShowEstimatedDuration = true,
                ProgressCharacter = '*',
                CollapseWhenFinished = true,
            });
        downloading.EstimatedDuration = TimeSpan.FromSeconds(5);
        using var installing = Console.StartProgressBar("installing");

        for (var i = 0; i < 11; i++)
        {
            downloading.Tick(i * 20);
            installing.Tick(i * 10);
            main.Tick(i * 10);
            await Task.Delay(1000);
        }

        main.Tick("your game is ready!");
        return 0;
    }
}
