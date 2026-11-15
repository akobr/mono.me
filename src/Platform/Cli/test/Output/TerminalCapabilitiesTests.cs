using System;
using System.Collections.Generic;
using System.Text;
using _42.Platform.Cli.Output;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// What sform reads of the terminal from its environment (spec section 7.4): 24-bit colors, emoji two cells wide and
// NO_COLOR, each by its variable.
public class TerminalCapabilitiesTests
{
    [Theory]
    [InlineData("COLORTERM", "truecolor", true)]
    [InlineData("COLORTERM", "24bit", true)]
    [InlineData("COLORTERM", "TrueColor", true)]
    [InlineData("COLORTERM", "256color", false)]
    [InlineData("COLORTERM", "", false)]
    [InlineData("WT_SESSION", "5c3e…", true)]
    [InlineData("TERM_PROGRAM", "vscode", false)]
    [InlineData("TERM", "xterm-256color", false)]
    public void TrueColor_FollowsColorTermAndWindowsTerminal(string variable, string value, bool expected)
    {
        TerminalCapabilities.SupportsTrueColor(Variables((variable, value))).ShouldBe(expected);
    }

    [Fact]
    public void TrueColor_WithoutAnyVariable_IsOff()
    {
        TerminalCapabilities.SupportsTrueColor(Variables()).ShouldBeFalse();
    }

    [Theory]
    [InlineData("WT_SESSION", "5c3e")]
    [InlineData("TERM_PROGRAM", "vscode")]
    [InlineData("TERM_PROGRAM", "iTerm.app")]
    [InlineData("TERM_PROGRAM", "WezTerm")]
    [InlineData("TERM_PROGRAM", "ghostty")]
    [InlineData("TERM_PROGRAM", "Apple_Terminal")]
    [InlineData("TERM_PROGRAM", "Hyper")]
    [InlineData("TERM", "xterm-kitty")]
    [InlineData("TERM", "xterm-ghostty")]
    [InlineData("TERM", "wezterm")]
    [InlineData("TERM", "alacritty")]
    [InlineData("TERM", "foot")]
    [InlineData("VTE_VERSION", "7600")]
    [InlineData("KONSOLE_VERSION", "230804")]
    public void Emoji_AreDrawnWide_ByTheKnownTerminals(string variable, string value)
    {
        TerminalCapabilities.DrawsEmojiWide(Variables((variable, value))).ShouldBeTrue();
    }

    // The classic Windows console host sets none of them; an unknown terminal may draw emoji in one cell.
    [Theory]
    [InlineData("TERM_PROGRAM", "tmux")]
    [InlineData("TERM", "xterm-256color")]
    [InlineData("TERM", "screen")]
    [InlineData("COLORTERM", "truecolor")]
    [InlineData("VTE_VERSION", " ")]
    public void Emoji_AreNotDrawnWide_ByOtherTerminals(string variable, string value)
    {
        TerminalCapabilities.DrawsEmojiWide(Variables((variable, value))).ShouldBeFalse();
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NoColor_IsAnyNonEmptyValue(string? value, bool expected)
    {
        TerminalCapabilities.IsNoColor(Variables(("NO_COLOR", value))).ShouldBe(expected);
    }

    [Fact]
    public void Utf8_IsTheEncodingOfCodePage65001()
    {
        TerminalCapabilities.IsUtf8(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ShouldBeTrue();
        TerminalCapabilities.IsUtf8(Encoding.UTF8).ShouldBeTrue();
        TerminalCapabilities.IsUtf8(Encoding.Unicode).ShouldBeFalse();
        TerminalCapabilities.IsUtf8(Encoding.ASCII).ShouldBeFalse();
        TerminalCapabilities.IsUtf8(null).ShouldBeFalse();
    }

    internal static Func<string, string?> Variables(params (string Name, string? Value)[] variables)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (name, value) in variables)
        {
            values[name] = value;
        }

        return name => values.GetValueOrDefault(name);
    }
}
