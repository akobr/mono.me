using System;
using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit.Output;

public class ConsoleOutputText : IConsoleOutput
{
    private readonly string _text;
    private readonly ConsoleColor? _color;

    public ConsoleOutputText(string text)
    {
        _text = text;
    }

    public ConsoleOutputText(string text, ConsoleColor color)
    {
        _text = text;
        _color = color;
    }

    public static implicit operator ConsoleOutputText(string text)
    {
        return new ConsoleOutputText(text);
    }

    public void WriteTo(IExtendedConsole console)
    {
        var tConsole = console.Console;

        if (!_color.HasValue)
        {
            tConsole.Write(_text);
            return;
        }

        var foregroundColor = tConsole.ForegroundColor;
        tConsole.ForegroundColor = _color.Value;
        tConsole.Write(_text);
        tConsole.ForegroundColor = foregroundColor;
    }
}
