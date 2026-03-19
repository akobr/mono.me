using System;
using Spectre.Console;

namespace _42.CLI.Toolkit.Output;

internal static class SpectreColorHelper
{
    public static Color ToSpectreColor(ConsoleColor consoleColor)
    {
        return consoleColor switch
        {
            ConsoleColor.Black => Color.Black,
            ConsoleColor.DarkBlue => Color.Navy,
            ConsoleColor.DarkGreen => Color.Green,
            ConsoleColor.DarkCyan => Color.Teal,
            ConsoleColor.DarkRed => Color.Maroon,
            ConsoleColor.DarkMagenta => Color.Purple,
            ConsoleColor.DarkYellow => Color.Olive,
            ConsoleColor.Gray => Color.Silver,
            ConsoleColor.DarkGray => Color.Grey,
            ConsoleColor.Blue => Color.Blue,
            ConsoleColor.Green => Color.Lime,
            ConsoleColor.Cyan => Color.Aqua,
            ConsoleColor.Red => Color.Red,
            ConsoleColor.Magenta => Color.Fuchsia,
            ConsoleColor.Yellow => Color.Yellow,
            ConsoleColor.White => Color.White,
            _ => Color.Default,
        };
    }
}
