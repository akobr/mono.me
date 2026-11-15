using System;
using System.Collections.Generic;
using System.Text;
using _42.Platform.Cli.Configuration;

namespace _42.Platform.Cli.Output;

// What the terminal of a run can show of the project settings, from its environment variables: colors at all (NO_COLOR),
// 24-bit colors, and emoji two cells wide. A terminal that is not known to draw emoji that wide (the classic Windows
// console host, an unknown one) gets no icons in auto mode, so its columns stay aligned.
internal static class TerminalCapabilities
{
    // Windows Terminal: 24-bit colors and emoji.
    public const string WT_SESSION = "WT_SESSION";

    // truecolor or 24bit: a terminal with 24-bit colors.
    public const string COLORTERM = "COLORTERM";

    public const string TERM_PROGRAM = "TERM_PROGRAM";
    public const string TERM = "TERM";

    // GNOME Terminal and the other terminals on VTE, and Konsole.
    public const string VTE_VERSION = "VTE_VERSION";
    public const string KONSOLE_VERSION = "KONSOLE_VERSION";

    private const int Utf8CodePage = 65001;

    private static readonly HashSet<string> TrueColorTerms = new(StringComparer.OrdinalIgnoreCase) { "truecolor", "24bit" };

    private static readonly HashSet<string> EmojiTermPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "vscode",
        "iTerm.app",
        "WezTerm",
        "ghostty",
        "Apple_Terminal",
        "Hyper",
    };

    private static readonly HashSet<string> EmojiTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "xterm-kitty",
        "xterm-ghostty",
        "wezterm",
        "alacritty",
        "foot",
    };

    // NO_COLOR with any non-empty value (https://no-color.org).
    public static bool IsNoColor(Func<string, string?> getVariable)
    {
        return !string.IsNullOrEmpty(getVariable(SformVariables.NO_COLOR));
    }

    // COLORTERM=truecolor|24bit, or Windows Terminal.
    public static bool SupportsTrueColor(Func<string, string?> getVariable)
    {
        return IsSet(getVariable, WT_SESSION)
            || (getVariable(COLORTERM)?.Trim() is { Length: > 0 } colorTerm && TrueColorTerms.Contains(colorTerm));
    }

    // A terminal known to draw emoji two cells wide: Windows Terminal, the TERM_PROGRAM and TERM values of the list,
    // a VTE terminal or Konsole.
    public static bool DrawsEmojiWide(Func<string, string?> getVariable)
    {
        return IsSet(getVariable, WT_SESSION)
            || IsSet(getVariable, VTE_VERSION)
            || IsSet(getVariable, KONSOLE_VERSION)
            || (getVariable(TERM_PROGRAM)?.Trim() is { Length: > 0 } program && EmojiTermPrograms.Contains(program))
            || (getVariable(TERM)?.Trim() is { Length: > 0 } term && EmojiTerms.Contains(term));
    }

    // Whether text written with the encoding reaches the terminal as UTF-8 (an emoji would be '?' in a code page).
    public static bool IsUtf8(Encoding? encoding)
    {
        return encoding?.CodePage == Utf8CodePage;
    }

    private static bool IsSet(Func<string, string?> getVariable, string name)
    {
        return !string.IsNullOrWhiteSpace(getVariable(name));
    }
}
