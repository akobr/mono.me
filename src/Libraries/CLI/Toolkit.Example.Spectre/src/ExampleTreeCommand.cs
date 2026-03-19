using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit.Example;

[Command("tree", Description = "Example of trees.")]
public class ExampleTreeCommand : BaseCommand
{
    public ExampleTreeCommand(IExtendedConsole console)
        : base(console)
    {
        // no operation
    }

    public override Task<int> OnExecuteAsync()
    {
        SimpleTree();
        Console.WriteLine();
        ComplexTree();

        return Task.FromResult(0);
    }

    private void SimpleTree()
    {
        var tree = new Composition("small");

        tree.Children.AddRange(new[]
        {
            new Composition("first"),
            new Composition("second"),
            new Composition("third"),
        });

        tree.Children[1].Children.Add(new Composition("42"));
        Console.WriteTree(tree);
    }

    private void ComplexTree()
    {
        var tree = new Composition("large");

        tree.Children.AddRange(new[]
        {
            new Composition("first"),
            new Composition("second"),
            new Composition("third"),
            new Composition("fourth"),
            new Composition("fifth"),
        });

        tree.Children[0].Children.AddRange(new[]
        {
            new Composition("10"),
            new Composition("11"),
        });

        tree.Children[1].Children.AddRange(new[]
        {
            new Composition("20"),
            new Composition("21"),
            new Composition("22"),
            new Composition("23"),
            new Composition("24"),
            new Composition("25"),
        });

        tree.Children[2].Children.AddRange(new[]
        {
            new Composition("30"),
            new Composition("31"),
            new Composition("32"),
            new Composition("33"),
        });

        tree.Children[3].Children.Add(new Composition("42"));
        tree.Children[3].Children[0].Children.AddRange(new[]
        {
            new Composition("the".Lowlight()),
            new Composition("answer".Highlight()),
        });

        tree.Children[4].Children.AddRange(new[]
        {
            new Composition("50"),
            new Composition("51"),
            new Composition("52"),
            new Composition("53"),
            new Composition("54"),
            new Composition("55"),
            new Composition("56"),
            new Composition("57"),
            new Composition("58"),
            new Composition("59"),
        });

        Console.WriteTree(tree);
    }
}
