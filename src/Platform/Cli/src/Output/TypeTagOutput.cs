using System;
using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Output;

// The tag of a tree node with the project's appearance of its type: " 🏢 [Company]" in the type's color, then the status
// of the annotation in the lowlight color. The color is the 24-bit shade (written as escape codes, which only a terminal
// that takes them gets), else the nearest console color, else (NO_COLOR) the lowlight color of today's tags.
internal sealed class TypeTagOutput : IConsoleOutput
{
    private readonly string _tag;
    private readonly string? _symbol;
    private readonly string? _color;
    private readonly bool _isTrueColor;
    private readonly string _suffix;

    public TypeTagOutput(string tag, string? symbol, string? color, bool isTrueColor, string suffix)
    {
        _tag = tag;
        _symbol = symbol;
        _color = color;
        _isTrueColor = isTrueColor;
        _suffix = suffix;
    }

    public void WriteTo(IExtendedConsole console)
    {
        var target = console.Console;
        target.Write(_symbol is null ? " " : $" {_symbol} ");

        if (_isTrueColor && TypeColors.TryGetRgb(_color, out var rgb))
        {
            target.Write($"{TypeColors.TrueColorEscape(rgb)}{_tag}{TypeColors.DefaultForegroundEscape}");
        }
        else
        {
            WriteColored(console, _tag, TypeColors.TryGetConsoleColor(_color, out var color) ? color : console.Theme.LowlightColor);
        }

        if (_suffix.Length > 0)
        {
            WriteColored(console, _suffix, console.Theme.LowlightColor);
        }
    }

    private static void WriteColored(IExtendedConsole console, string text, ConsoleColor color)
    {
        var target = console.Console;
        var foregroundColor = target.ForegroundColor;
        target.ForegroundColor = color;
        target.Write(text);
        target.ForegroundColor = foregroundColor;
    }
}
