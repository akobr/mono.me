using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit.Example;

[Subcommand(
    typeof(ExampleBasicCommand),
    typeof(ExampleTableCommand),
    typeof(ExampleTreeCommand),
    typeof(ExampleInputCommand),
    typeof(ExampleProgressCommand))]

[Command("example", Description = "A parent command for all examples.")]
public class ExampleParentCommand : BaseParentCommand
{
    public ExampleParentCommand(
        IExtendedConsole console,
        CommandLineApplication application)
        : base(console, application)
    {
        // no operation
    }
}
