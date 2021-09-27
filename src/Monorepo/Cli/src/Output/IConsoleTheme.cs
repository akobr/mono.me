using System;

namespace _42.Monorepo.Cli.Output
{
    public interface IConsoleTheme
    {
        ConsoleColor ForegroundColor { get; }

        ConsoleColor HeaderColor { get; }

        ConsoleColor HighlightColor { get; }

        ConsoleColor LowlightColor { get; }
    }
}
