using System.Collections.Generic;

namespace _42.CLI.Toolkit.Output;

public class ConsoleOutputComposition : IConsoleOutput
{
    private readonly IEnumerable<IConsoleOutput> _children;

    public ConsoleOutputComposition(IEnumerable<IConsoleOutput> children)
    {
        this._children = children;
    }

    public ConsoleOutputComposition(params IConsoleOutput[] children)
    {
        this._children = children;
    }

    public static implicit operator ConsoleOutputComposition(IConsoleOutput[] items)
    {
        return new ConsoleOutputComposition(items);
    }

    public void WriteTo(IExtendedConsole console)
    {
        foreach (var child in _children)
        {
            child.WriteTo(console);
        }
    }
}
