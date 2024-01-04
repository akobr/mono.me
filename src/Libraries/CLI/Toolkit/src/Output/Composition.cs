using System.Collections.Generic;
using System.Linq;

namespace _42.CLI.Toolkit.Output;

public class Composition<T> : IComposition<T>
{
    public Composition(T content)
    {
        Content = content;
        Children = new List<IComposition<T>>();
    }

    public T Content { get; set; }

    public List<IComposition<T>> Children { get; set; }

    IReadOnlyCollection<IComposition<T>> IComposition<T>.Children => Children;
}

public class Composition : IComposition<IConsoleOutput>
{
    public Composition(IConsoleOutput content)
    {
        Content = content;
        Children = new List<Composition>();
    }

    public Composition(ConsoleOutput content)
        : this((IConsoleOutput)content)
    {
    }

    public IConsoleOutput Content { get; set; }

    public List<Composition> Children { get; set; }

    IReadOnlyCollection<IComposition<IConsoleOutput>> IComposition<IConsoleOutput>.Children => Children;

    public static implicit operator Composition(ConsoleOutput content)
    {
        return new Composition(content);
    }

    public static implicit operator Composition(string text)
    {
        return new Composition(new ConsoleOutputText(text));
    }

    public static implicit operator Composition(IConsoleOutput[] items)
    {
        return new Composition(new ConsoleOutputComposition(items));
    }

    public static implicit operator Composition(object[] items)
    {
        return new Composition(new ConsoleOutputComposition(items.Select(i => new ConsoleOutput(i))));
    }
}
