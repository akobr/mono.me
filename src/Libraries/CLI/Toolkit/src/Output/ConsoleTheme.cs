using System;

namespace _42.CLI.Toolkit.Output;

public class ConsoleTheme : IConsoleTheme
{
    public ConsoleColor ForegroundColor { get; init; }

    public ConsoleColor HeaderColor { get; init; }

    public ConsoleColor HighlightColor { get; init; }

    public ConsoleColor LowlightColor { get; init; }

    public ConsoleColor ErrorColor { get; init; }
}
