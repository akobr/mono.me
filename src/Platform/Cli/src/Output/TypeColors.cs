using System;
using System.Collections.Generic;

namespace _42.Platform.Cli.Output;

// The colors of annotation types, the 18 names of the catalog of project settings
// (src/Platform/Storyteller/conformance/project-settings.json, in the admin UI's 500 shades), as the nearest of the 16
// colors of a console. DarkGray, the theme's lowlight, is left out, so a type never looks like a hint.
internal static class TypeColors
{
    private static readonly Dictionary<string, ConsoleColor> ConsoleColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = ConsoleColor.Red,
        ["orange"] = ConsoleColor.DarkYellow,
        ["amber"] = ConsoleColor.Yellow,
        ["yellow"] = ConsoleColor.Yellow,
        ["lime"] = ConsoleColor.Green,
        ["green"] = ConsoleColor.Green,
        ["emerald"] = ConsoleColor.Green,
        ["teal"] = ConsoleColor.DarkCyan,
        ["cyan"] = ConsoleColor.Cyan,
        ["sky"] = ConsoleColor.Cyan,
        ["blue"] = ConsoleColor.Blue,
        ["indigo"] = ConsoleColor.DarkBlue,
        ["violet"] = ConsoleColor.DarkMagenta,
        ["purple"] = ConsoleColor.DarkMagenta,
        ["fuchsia"] = ConsoleColor.Magenta,
        ["pink"] = ConsoleColor.Magenta,
        ["rose"] = ConsoleColor.Red,
        ["gray"] = ConsoleColor.Gray,
    };

    public static IReadOnlyCollection<string> Names => ConsoleColors.Keys;

    // The console color of a catalog color, in any case; false for any other name.
    public static bool TryGetConsoleColor(string? color, out ConsoleColor consoleColor)
    {
        consoleColor = default;
        return color is not null && ConsoleColors.TryGetValue(color, out consoleColor);
    }
}
