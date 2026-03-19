using System;
using Spectre.Console;

namespace _42.CLI.Toolkit.Output;

public class ConsoleOutputThemedText : IConsoleOutput
{
    private readonly string _text;
    private readonly Func<IConsoleTheme, ConsoleColor> _colorProvider;

    public ConsoleOutputThemedText(string text, Func<IConsoleTheme, ConsoleColor> colorProvider)
    {
        _text = text;
        _colorProvider = colorProvider;
    }

    public void WriteTo(IExtendedConsole console)
    {
        var color = _colorProvider(console.Theme);
        var spectreColor = SpectreColorHelper.ToSpectreColor(color);
        console.AnsiConsole.Write(new Text(_text, new Style(foreground: spectreColor)));
    }
}
