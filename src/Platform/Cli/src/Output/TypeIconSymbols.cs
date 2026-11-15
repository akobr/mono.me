using System;
using System.Collections.Generic;

namespace _42.Platform.Cli.Output;

// The icons of annotation types, the Lucide names of the catalog of project settings
// (src/Platform/Storyteller/conformance/project-settings.json), as the emoji a terminal draws for them. Each symbol is
// exactly two UTF-16 code units with emoji presentation: a surrogate pair, or a symbol of the basic plane followed by
// U+FE0F. A terminal that draws emoji two cells wide shows each in two cells, so the length of a string is its width
// in a table. A unit test checks that every icon of the catalog has a symbol of its own.
internal static class TypeIconSymbols
{
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["i-lucide-target"] = "\U0001F3AF",
        ["i-lucide-box"] = "\U0001F4E6",
        ["i-lucide-sun"] = "☀️",
        ["i-lucide-link"] = "\U0001F517",
        ["i-lucide-layers"] = "\U0001F4DA",
        ["i-lucide-play"] = "▶️",
        ["i-lucide-cpu"] = "\U0001F4BB",
        ["i-lucide-activity"] = "\U0001F4C8",
        ["i-lucide-anchor"] = "⚓️",
        ["i-lucide-atom"] = "⚛️",
        ["i-lucide-bell"] = "\U0001F514",
        ["i-lucide-bookmark"] = "\U0001F4D1",
        ["i-lucide-bot"] = "\U0001F916",
        ["i-lucide-boxes"] = "\U0001F9F1",
        ["i-lucide-brain"] = "\U0001F9E0",
        ["i-lucide-briefcase"] = "\U0001F4BC",
        ["i-lucide-bug"] = "\U0001F41B",
        ["i-lucide-building-2"] = "\U0001F3E2",
        ["i-lucide-calendar"] = "\U0001F4C5",
        ["i-lucide-circle"] = "\U0001F535",
        ["i-lucide-clock"] = "\U0001F552",
        ["i-lucide-cloud"] = "☁️",
        ["i-lucide-code"] = "⌨️",
        ["i-lucide-cog"] = "⚙️",
        ["i-lucide-compass"] = "\U0001F9ED",
        ["i-lucide-component"] = "\U0001F4A0",
        ["i-lucide-container"] = "\U0001F6A2",
        ["i-lucide-crown"] = "\U0001F451",
        ["i-lucide-database"] = "\U0001F4BE",
        ["i-lucide-factory"] = "\U0001F3ED",
        ["i-lucide-file"] = "\U0001F4C4",
        ["i-lucide-flag"] = "\U0001F6A9",
        ["i-lucide-flame"] = "\U0001F525",
        ["i-lucide-folder"] = "\U0001F4C1",
        ["i-lucide-gauge"] = "⏱️",
        ["i-lucide-gem"] = "\U0001F48E",
        ["i-lucide-git-branch"] = "\U0001F500",
        ["i-lucide-globe"] = "\U0001F310",
        ["i-lucide-heart"] = "❤️",
        ["i-lucide-hexagon"] = "\U0001F537",
        ["i-lucide-key"] = "\U0001F511",
        ["i-lucide-landmark"] = "\U0001F3E6",
        ["i-lucide-layout-grid"] = "\U0001F533",
        ["i-lucide-leaf"] = "\U0001F343",
        ["i-lucide-lightbulb"] = "\U0001F4A1",
        ["i-lucide-map-pin"] = "\U0001F4CD",
        ["i-lucide-monitor"] = "\U0001F4FA",
        ["i-lucide-network"] = "\U0001F4E1",
        ["i-lucide-package"] = "\U0001F381",
        ["i-lucide-puzzle"] = "\U0001F9E9",
        ["i-lucide-rocket"] = "\U0001F680",
        ["i-lucide-server"] = "\U0001F4BD",
        ["i-lucide-shield"] = "\U0001F512",
        ["i-lucide-smartphone"] = "\U0001F4F1",
        ["i-lucide-square"] = "\U0001F7E6",
        ["i-lucide-star"] = "⭐️",
        ["i-lucide-store"] = "\U0001F3EA",
        ["i-lucide-tag"] = "\U0001F516",
        ["i-lucide-triangle"] = "\U0001F53A",
        ["i-lucide-truck"] = "\U0001F69A",
        ["i-lucide-user"] = "\U0001F464",
        ["i-lucide-users"] = "\U0001F465",
        ["i-lucide-workflow"] = "\U0001F504",
        ["i-lucide-wrench"] = "\U0001F527",
        ["i-lucide-zap"] = "⚡️",
    };

    public static IReadOnlyCollection<string> Icons => Symbols.Keys;

    // The symbol of a catalog icon, in any case; false for any other name (an icon of a newer catalog).
    public static bool TryGetSymbol(string? icon, out string symbol)
    {
        symbol = string.Empty;
        return icon is not null && Symbols.TryGetValue(icon, out symbol!);
    }
}
