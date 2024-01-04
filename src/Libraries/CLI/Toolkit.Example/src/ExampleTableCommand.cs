using System.Reflection;
using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit.Example;

[Command("table", Description = "Example of table.")]
public class ExampleTableCommand : BaseCommand
{
    public ExampleTableCommand(IExtendedConsole console)
        : base(console)
    {
        // no operation
    }

    public override Task<int> OnExecuteAsync()
    {
        var index = 0;
        var commands = Assembly.GetEntryAssembly()!.GetTypes()
            .Where(type => type.IsClass && type.GetCustomAttributes(typeof(CommandAttribute)).Any());


        Console.WriteTable(
            commands,
            command => new[]
            {
                (++index).ToString(),
                command.Name,
                command.GUID.ToString("D"),
            },
            new[]
            {
                "Number",
                "Name",
                "GUID",
            });

        return Task.FromResult(0);
    }
}
