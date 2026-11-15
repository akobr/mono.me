using System;
using System.Globalization;
using System.Linq;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// The tables of sform against the catalog of project settings (src/Platform/Storyteller/conformance/project-settings.json,
// embedded in Abstractions.Annotations): every color has a 24-bit shade and a console color, every icon a symbol that a
// terminal draws in two cells.
public class TypeAppearanceTablesTests
{
    private static readonly ProjectSettingsCatalog Catalog = ProjectSettingsCatalog.Default;

    [Fact]
    public void TypeColors_HaveEveryColorOfTheCatalog_AndNoOther()
    {
        TypeColors.Names.Order(StringComparer.Ordinal).ShouldBe(Catalog.Colors.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TypeColors_GiveEachColorAShadeAndAConsoleColor_NeverTheLowlight()
    {
        foreach (var color in Catalog.Colors)
        {
            TypeColors.TryGetConsoleColor(color, out var consoleColor).ShouldBeTrue(color);
            consoleColor.ShouldNotBe(ConsoleColor.DarkGray, color);
            TypeColors.TryGetRgb(color.ToUpperInvariant(), out var rgb).ShouldBeTrue(color);
            rgb.ShouldBeInRange(0, 0xffffff, color);
        }

        TypeColors.TryGetConsoleColor("magenta", out _).ShouldBeFalse();
        TypeColors.TryGetRgb(null, out _).ShouldBeFalse();
    }

    // The admin UI's 500 shades (Tailwind CSS 4): blue-500 is oklch(62.3% 0.214 259.815), #2b7fff in sRGB.
    [Theory]
    [InlineData("blue", 0x2b7fff, ConsoleColor.Blue)]
    [InlineData("violet", 0x8e51ff, ConsoleColor.DarkMagenta)]
    [InlineData("emerald", 0x00bc7d, ConsoleColor.Green)]
    [InlineData("orange", 0xff6900, ConsoleColor.DarkYellow)]
    [InlineData("gray", 0x6a7282, ConsoleColor.Gray)]
    public void TypeColors_AreTheShadesOfTheAdminUi(string color, int rgb, ConsoleColor consoleColor)
    {
        TypeColors.TryGetRgb(color, out var shade).ShouldBeTrue();
        shade.ShouldBe(rgb);
        TypeColors.TryGetConsoleColor(color, out var nearest).ShouldBeTrue();
        nearest.ShouldBe(consoleColor);
    }

    [Fact]
    public void TrueColorEscape_SetsTheForegroundToTheShade()
    {
        TypeColors.TrueColorEscape(0x2b7fff).ShouldBe("\u001b[38;2;43;127;255m");
        TypeColors.DefaultForegroundEscape.ShouldBe("\u001b[39m");
    }

    [Fact]
    public void TypeIconSymbols_HaveEveryIconOfTheCatalog_AndNoOther()
    {
        TypeIconSymbols.Icons.Order(StringComparer.Ordinal).ShouldBe(Catalog.Icons.Order(StringComparer.Ordinal));
    }

    // Two UTF-16 code units, so the length of a cell's text is the two cells the emoji takes: a surrogate pair of a
    // symbol of emoji presentation, or a symbol of the basic plane with the emoji variation selector U+FE0F.
    [Fact]
    public void TypeIconSymbols_AreEmojiOfTwoCodeUnits()
    {
        foreach (var icon in Catalog.Icons)
        {
            TypeIconSymbols.TryGetSymbol(icon, out var symbol).ShouldBeTrue(icon);
            symbol.Length.ShouldBe(2, icon);

            var isSurrogatePair = char.IsHighSurrogate(symbol[0]) && char.IsLowSurrogate(symbol[1]);
            var isVariation = !char.IsSurrogate(symbol[0]) && symbol[1] == '️';
            (isSurrogatePair || isVariation).ShouldBeTrue(icon);

            if (isSurrogatePair)
            {
                // the pictographs and symbols of the supplementary planes that terminals draw as emoji
                char.ConvertToUtf32(symbol[0], symbol[1]).ShouldBeInRange(0x1F300, 0x1FAFF, icon);
            }

            CharUnicodeInfo.GetUnicodeCategory(symbol, 0).ShouldBe(UnicodeCategory.OtherSymbol, icon);
        }
    }

    [Fact]
    public void TypeIconSymbols_AreDistinct_AndFoundInAnyCase()
    {
        var symbols = Catalog.Icons.Select(icon => TypeIconSymbols.TryGetSymbol(icon, out var symbol) ? symbol : null).ToList();

        symbols.Distinct(StringComparer.Ordinal).Count().ShouldBe(Catalog.Icons.Count);
        TypeIconSymbols.TryGetSymbol("I-LUCIDE-BUILDING-2", out var building).ShouldBeTrue();
        building.ShouldBe("\U0001F3E2");
        TypeIconSymbols.TryGetSymbol("i-lucide-of-a-newer-catalog", out _).ShouldBeFalse();
        TypeIconSymbols.TryGetSymbol(null, out _).ShouldBeFalse();
    }
}
