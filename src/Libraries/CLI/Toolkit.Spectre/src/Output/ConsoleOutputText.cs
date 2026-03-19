using System;
using Spectre.Console;

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
        if (!_color.HasValue)
        {
            console.AnsiConsole.Write(new Text(_text));
            return;
        }

        var spectreColor = SpectreColorHelper.ToSpectreColor(_color.Value);
        console.AnsiConsole.Write(new Text(_text, new Style(foreground: spectreColor)));
    }
}
