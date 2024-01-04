using System;
using McMaster.Extensions.CommandLineUtils;

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
        var tConsole = console.Console;

        var foregroundColor = tConsole.ForegroundColor;
        tConsole.ForegroundColor = _colorProvider(console.Theme);
        tConsole.Write(_text);
        tConsole.ForegroundColor = foregroundColor;
    }
}
