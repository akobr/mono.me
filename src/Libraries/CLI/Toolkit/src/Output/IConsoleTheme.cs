using System;

namespace _42.CLI.Toolkit.Output;

public interface IConsoleTheme
{
    ConsoleColor ForegroundColor { get; }

    ConsoleColor HeaderColor { get; }

    ConsoleColor HighlightColor { get; }

    ConsoleColor LowlightColor { get; }

    ConsoleColor ErrorColor { get; }
}
