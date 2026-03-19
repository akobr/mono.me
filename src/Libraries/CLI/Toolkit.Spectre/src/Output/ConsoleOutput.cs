using System.Linq;

namespace _42.CLI.Toolkit.Output;

public class ConsoleOutput : IConsoleOutput
{
    private readonly IConsoleOutput _wrappedOutput;

    public ConsoleOutput(IConsoleOutput text)
    {
        _wrappedOutput = text;
    }

    public ConsoleOutput(object? item)
    {
        switch (item)
        {
            case null:
                _wrappedOutput = new EmptyConsoleOutput();
                break;

            case IConsoleOutput writable:
                _wrappedOutput = writable;
                break;

            default:
                var text = item.ToString();
                _wrappedOutput = text is not null
                    ? new ConsoleOutputText(text)
                    : new EmptyConsoleOutput();
                break;
        }
    }

    public static implicit operator ConsoleOutput(string text)
    {
        return new ConsoleOutput(new ConsoleOutputText(text));
    }

    public static implicit operator ConsoleOutput(IConsoleOutput[] items)
    {
        return new ConsoleOutput(new ConsoleOutputComposition(items));
    }

    public static implicit operator ConsoleOutput(object[] items)
    {
        return new ConsoleOutput(new ConsoleOutputComposition(items.Select(i => new ConsoleOutput(i))));
    }

    public void WriteTo(IExtendedConsole console)
    {
        _wrappedOutput.WriteTo(console);
    }
}
