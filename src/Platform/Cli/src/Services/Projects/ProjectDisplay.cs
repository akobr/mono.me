using System;
using System.Collections.Generic;
using System.Globalization;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Projects;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller;
using Alba.CsConsoleFormat;
using Appearance = _42.Platform.Storyteller.ProjectTypeAppearance;

namespace _42.Platform.Cli.Services.Projects;

// How the console output for people shows the annotation types and the dates of a project (spec section 7.2): the
// project's names of its types (list headers, tree tags, type columns, composition sources, import tables), their colors
// and icons (tree tags and type columns), and its date format (dates in tables and the deleted notices). The text of a
// type the project does not name stays as today; colors and icons appear only when the project changes at least one
// type, and only as far as the terminal shows them (ProjectSettingsProvider). BuiltIn, and a project without settings,
// write today's output byte for byte. Nothing here changes what sform accepts or writes to files and programs.
public sealed class ProjectDisplay
{
    // The date and time formats of today, the built-in display.
    private const string BuiltInDatePattern = "yyyy-MM-dd";
    private const string TimePattern = "HH:mm";

    // The .NET patterns of the catalog's date formats, with the invariant culture: English months, literal separators.
    private static readonly Dictionary<string, string> DatePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yyyy-mm-dd"] = BuiltInDatePattern,
        ["dd.mm.yyyy"] = "dd'.'MM'.'yyyy",
        ["dd/mm/yyyy"] = "dd'/'MM'/'yyyy",
        ["mm/dd/yyyy"] = "MM'/'dd'/'yyyy",
        ["d mmm yyyy"] = "d MMM yyyy",
    };

    private readonly IReadOnlyDictionary<string, Appearance> _types;
    private readonly string _datePattern;

    private ProjectDisplay(IReadOnlyDictionary<string, Appearance> types, string? dateFormat, TypeColorMode colors, bool showsIcons)
    {
        _types = types;
        DateFormat = dateFormat is not null && DatePatterns.ContainsKey(dateFormat) ? dateFormat : null;
        _datePattern = DateFormat is null ? BuiltInDatePattern : DatePatterns[DateFormat];
        Colors = types.Count > 0 ? colors : TypeColorMode.None;
        ShowsIcons = types.Count > 0 && showsIcons;
    }

    // The built-in names and today's dates, without colors and icons: the output of sform before project settings.
    public static ProjectDisplay BuiltIn { get; } = new(new Dictionary<string, Appearance>(), null, TypeColorMode.None, false);

    // The date format of the project as the catalog spells it; null for the built-in yyyy-mm-dd (also for a format this
    // version of sform does not know).
    public string? DateFormat { get; }

    // The project changes the name, color or icon of at least one type.
    public bool HasTypes => _types.Count > 0;

    // How type colors are written; None without types, with NO_COLOR, or without a console.
    public TypeColorMode Colors { get; }

    // Whether the icons of the types are shown; never without types.
    public bool ShowsIcons { get; }

    // Shows nothing but the built-in names and dates: what BuiltIn shows.
    public bool IsBuiltIn => !HasTypes && DateFormat is null;

    // The display of a project's settings (as the API answers them, or as the cache keeps them): the entries of the
    // catalog's type codes (in any case) that set any value, and the date format.
    public static ProjectDisplay Create(IReadOnlyDictionary<string, Appearance>? types, string? dateFormat, TypeColorMode colors, bool showsIcons)
    {
        var known = new Dictionary<string, Appearance>(StringComparer.Ordinal);
        var catalog = ProjectSettingsCatalog.Default;

        foreach (var (key, appearance) in types ?? new Dictionary<string, Appearance>())
        {
            if (appearance is null || !catalog.TryGetTypeCode(key, out var code))
            {
                continue;
            }

            var normalized = new Appearance
            {
                Label = Text(appearance.Label),
                Plural = Text(appearance.Plural),
                Color = Text(appearance.Color),
                Icon = Text(appearance.Icon),
            };

            if (normalized.Label is not null || normalized.Plural is not null || normalized.Color is not null || normalized.Icon is not null)
            {
                known[code] = normalized;
            }
        }

        var format = catalog.TryGetDateFormat(dateFormat, out var canonical) && canonical != catalog.DefaultDateFormat ? canonical : null;
        return new ProjectDisplay(known, format, colors, showsIcons);
    }

    // The project's name of a type (singular); null when the project gives none.
    public string? LabelOf(string code)
    {
        return Find(code)?.Label;
    }

    // The project's name of a type, else the text the output shows today (an enum name, a lower-case tag).
    public string NameOr(string code, string builtInText)
    {
        return LabelOf(code) ?? builtInText;
    }

    // The plural of a type: the project's plural, else its name made plural, else the built-in plural ("Subjects").
    public string PluralOf(string code)
    {
        var own = Find(code);

        if (own?.Plural is { } plural)
        {
            return plural;
        }

        if (own?.Label is { } label)
        {
            return ProjectSettingsDocuments.Pluralize(label);
        }

        return ProjectSettingsCatalog.Default.BuiltIn.TryGetValue(code, out var builtIn) && builtIn.Plural is { } builtInPlural
            ? builtInPlural
            : code;
    }

    // The catalog color of a type (the project's, else the built-in one); null when colors are not shown.
    public string? ColorOf(string code)
    {
        return Colors == TypeColorMode.None ? null : Find(code)?.Color ?? BuiltInOf(code)?.Color;
    }

    // The symbol of a type's icon (the project's, else the built-in one); null when icons are not shown or the icon is
    // not one this version of sform knows.
    public string? SymbolOf(string code)
    {
        return ShowsIcons && TypeIconSymbols.TryGetSymbol(Find(code)?.Icon ?? BuiltInOf(code)?.Icon, out var symbol)
            ? symbol
            : null;
    }

    // The tag of a tree node, " [subject]" and its status in the lowlight color today; with types, the icon, then the
    // project's name of the type (or today's tag) in the type's color, then the status: " 🏢 [Company] [disabled]".
    public IConsoleOutput TreeTag(string code, string builtInTag, string suffix)
    {
        if (!HasTypes)
        {
            return new ConsoleOutputThemedText($" [{builtInTag}]{suffix}", theme => theme.LowlightColor);
        }

        return new TypeTagOutput($"[{NameOr(code, builtInTag)}]", SymbolOf(code), ColorOf(code), Colors == TypeColorMode.TrueColor, suffix);
    }

    // The cell of a type in a table: today's text; with types, the icon and the project's name (or today's text) in the
    // nearest console color of the type.
    public Cell TypeCell(string code, string builtInText)
    {
        if (!HasTypes)
        {
            return new Cell(builtInText) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) };
        }

        var name = new Span(NameOr(code, builtInText));

        if (TypeColors.TryGetConsoleColor(ColorOf(code), out var color))
        {
            name.Color = color;
        }

        return SymbolOf(code) is { } symbol
            ? new Cell(new Span($"{symbol} "), name) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) }
            : new Cell(name) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) };
    }

    // A day in local time, in the project's date format: 2026-11-15 today; 15.11.2026, 15/11/2026, 11/15/2026, 15 Nov 2026.
    public string FormatDate(DateTimeOffset time)
    {
        return time.ToLocalTime().ToString(_datePattern, CultureInfo.InvariantCulture);
    }

    // A time in local time, its date in the project's date format and the time as today: 15.11.2026 14:02.
    public string FormatDateTime(DateTimeOffset time)
    {
        return time.ToLocalTime().ToString($"{_datePattern} {TimePattern}", CultureInfo.InvariantCulture);
    }

    // A day the API names by its date (not an instant): as it is, in the project's date format. Today's text keeps the
    // current culture it always had.
    public string FormatDay(DateTimeOffset day)
    {
        return DateFormat is null
            ? day.ToString(BuiltInDatePattern, CultureInfo.CurrentCulture)
            : day.ToString(_datePattern, CultureInfo.InvariantCulture);
    }

    private static Appearance? BuiltInOf(string code)
    {
        return ProjectSettingsCatalog.Default.BuiltIn.TryGetValue(code, out var builtIn) ? builtIn : null;
    }

    private static string? Text(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private Appearance? Find(string code)
    {
        return _types.TryGetValue(code, out var appearance) ? appearance : null;
    }
}
