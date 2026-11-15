using System;
using System.Collections.Generic;
using System.Globalization;

namespace _42.Platform.Cli.Output;

// The colors of annotation types, the 18 names of the catalog of project settings
// (src/Platform/Storyteller/conformance/project-settings.json): the admin UI's 500 shades (Tailwind CSS 4, its oklch
// values in sRGB) for a terminal with 24-bit colors, and the nearest of the 16 colors of a console for every other one.
// DarkGray, the theme's lowlight, is left out, so a type never looks like a hint.
internal static class TypeColors
{
    // The escape code that sets the foreground back to the terminal's default: ESC[39m.
    public const string DefaultForegroundEscape = "\u001b[39m";

    private static readonly Dictionary<string, (ConsoleColor Console, int Rgb)> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = (ConsoleColor.Red, 0xfb2c36),
        ["orange"] = (ConsoleColor.DarkYellow, 0xff6900),
        ["amber"] = (ConsoleColor.Yellow, 0xfe9a00),
        ["yellow"] = (ConsoleColor.Yellow, 0xf0b100),
        ["lime"] = (ConsoleColor.Green, 0x7ccf00),
        ["green"] = (ConsoleColor.Green, 0x00c950),
        ["emerald"] = (ConsoleColor.Green, 0x00bc7d),
        ["teal"] = (ConsoleColor.DarkCyan, 0x00bba7),
        ["cyan"] = (ConsoleColor.Cyan, 0x00b8db),
        ["sky"] = (ConsoleColor.Cyan, 0x00a6f4),
        ["blue"] = (ConsoleColor.Blue, 0x2b7fff),
        ["indigo"] = (ConsoleColor.DarkBlue, 0x615fff),
        ["violet"] = (ConsoleColor.DarkMagenta, 0x8e51ff),
        ["purple"] = (ConsoleColor.DarkMagenta, 0xad46ff),
        ["fuchsia"] = (ConsoleColor.Magenta, 0xe12afb),
        ["pink"] = (ConsoleColor.Magenta, 0xf6339a),
        ["rose"] = (ConsoleColor.Red, 0xff2056),
        ["gray"] = (ConsoleColor.Gray, 0x6a7282),
    };

    public static IReadOnlyCollection<string> Names => Colors.Keys;

    // The console color of a catalog color, in any case; false for any other name.
    public static bool TryGetConsoleColor(string? color, out ConsoleColor consoleColor)
    {
        consoleColor = default;

        if (color is null || !Colors.TryGetValue(color, out var known))
        {
            return false;
        }

        consoleColor = known.Console;
        return true;
    }

    // The 500 shade of a catalog color as 0xRRGGBB, in any case; false for any other name.
    public static bool TryGetRgb(string? color, out int rgb)
    {
        rgb = default;

        if (color is null || !Colors.TryGetValue(color, out var known))
        {
            return false;
        }

        rgb = known.Rgb;
        return true;
    }

    // The escape code that sets the foreground to a 24-bit color: ESC[38;2;<r>;<g>;<b>m.
    public static string TrueColorEscape(int rgb)
    {
        return string.Create(CultureInfo.InvariantCulture, $"\u001b[38;2;{(rgb >> 16) & 0xff};{(rgb >> 8) & 0xff};{rgb & 0xff}m");
    }
}
