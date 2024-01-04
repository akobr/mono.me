using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.CLI.Toolkit.Example;

[Command("input", Description = "Examples of basic inputs.")]
public class ExampleInputCommand : BaseCommand
{
    public ExampleInputCommand(IExtendedConsole console)
        : base(console)
    {
        // no operation
    }

    public override Task<int> OnExecuteAsync()
    {
        var name = Console.Input<string>(new InputOptions<string>
        {
            Message = "What is your name?",
            DefaultValue = "John Doe",
            Placeholder = "Write name",
        });

        Console.WriteLine("Hello ", name.ThemedHighlight(Console.Theme));
        Console.WriteLine();

        var areYouOk = Console.Confirm("Are you ok");

        if (areYouOk)
        {
            Console.WriteLine("Good to know!");
        }
        else
        {
            Console.WriteLine("Sorry to hear that!");
        }

        return Task.FromResult(0);
    }
}
