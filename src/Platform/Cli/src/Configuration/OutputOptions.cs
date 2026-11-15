using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Cli.Configuration;

// The output section: how the console output for people shows the settings of the project (its type names, colors,
// icons and date format). Set by the preferences output.icons and output.projectSettings (sform account settings), by
// SFORM_output__icons and SFORM_output__projectSettings, and for one run by SFORM_ICONS, SFORM_PROJECT_SETTINGS and
// --builtin (OutputSwitches). Nothing here changes what sform accepts, writes to files or prints without a console.
public sealed class OutputOptions
{
    // The icons of the types: where the terminal is known to draw them (the default), always, or never.
    public const string ICONS_AUTO = "auto";
    public const string ICONS_ALWAYS = "always";
    public const string ICONS_NEVER = "never";

    // The settings of the project (the default), or the built-in names and dates.
    public const string PROJECT_SETTINGS_PROJECT = "project";
    public const string PROJECT_SETTINGS_BUILTIN = "builtin";

    public static IReadOnlyList<string> IconsValues { get; } = [ICONS_AUTO, ICONS_ALWAYS, ICONS_NEVER];

    public static IReadOnlyList<string> ProjectSettingsValues { get; } = [PROJECT_SETTINGS_PROJECT, PROJECT_SETTINGS_BUILTIN];

    public string? Icons { get; set; }

    public string? ProjectSettings { get; set; }

    // One of the values, in any case and without the surrounding white space, as the list spells it; null for anything else.
    public static string? Parse(string? value, IReadOnlyList<string> values)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text)
            ? null
            : values.FirstOrDefault(known => string.Equals(known, text, StringComparison.OrdinalIgnoreCase));
    }
}
