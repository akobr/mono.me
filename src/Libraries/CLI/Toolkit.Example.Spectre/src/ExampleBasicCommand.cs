using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit.Example;

[Command("basic", Description = "Examples of basic text rendering.")]
public class ExampleBasicCommand : BaseCommand
{
    public ExampleBasicCommand(IExtendedConsole console)
        : base(console)
    {
        // no operation
    }

    public override Task<int> OnExecuteAsync()
    {
        Console.WriteHeader("This is a header");
        Console.WriteLine("A text content, a paragraph. " +
                          "Amet ipsum velit lorem amet dolor ea magna quis et ea. Consetetur et augue. " +
                          "Sadipscing gubergren sea stet at nulla ea magna consectetuer eirmod autem accumsan gubergren in eos diam. " +
                          "Consetetur aliquyam at iriure nostrud erat labore sit dolores takimata invidunt wisi sed. " +
                          "Eros sadipscing ullamcorper vero magna lorem tincidunt et et diam diam est id. " +
                          "Elitr te invidunt dolor aliquyam. Labore in ut duis labore ex vero stet takimata. " +
                          "No nihil exerci. Eirmod ea eos suscipit hendrerit voluptua dolor eu duis.");

        Console.WriteImportant("The important line.");
        Console.WriteLine("Formatted text, where something can be ", "more".ThemedHighlight(Console.Theme), " or ", "less".ThemedLowlight(Console.Theme), " important.");

        return Task.FromResult(0);
    }
}
