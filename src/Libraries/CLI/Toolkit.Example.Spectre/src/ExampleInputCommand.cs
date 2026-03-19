using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

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
        var name = Console.Input<string>("What is your name?", "John Doe");

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

        var password = Console.Password("Enter a secret password");
        Console.WriteLine("Password length: ", password.Length.ToString().ThemedHighlight(Console.Theme));

        return Task.FromResult(0);
    }
}
