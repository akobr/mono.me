using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Show;

[Command(CommandNames.PACKAGES, Description = "Show all available packages for a current location.")]
public class ShowPackagesCommand : BaseCommand
{
    public ShowPackagesCommand(IExtendedConsole console, ICommandContext context)
        : base(console, context)
    {
        // no operation
    }

    protected override async Task<int> ExecuteAsync()
    {
        Console.WriteHeader("All available packages of ", Context.Item.Record.Identifier.Humanized.ThemedHighlight(Console.Theme));
        var externalDependencies = await Context.Item.GetExternalDependenciesAsync();

        if (externalDependencies.Count > 0)
        {
            Console.WriteLine();
            Console.WriteHeader("Packages");
            Console.WriteTable(
                externalDependencies.OrderBy(d => d.Name),
                d =>
                {
                    var name = d.IsDirect ? $"> {d.Name}" : $"- {d.Name}";
                    return new[] { name, d.Version.ToString() };
                },
                new[] { "Package", "Version" });
        }

        return ExitCodes.SUCCESS;
    }
}
