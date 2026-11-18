using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Storyteller;

// The rules of project settings, the same for the API, sform, DbCreator and (by the vectors of the conformance suite,
// src/Platform/Storyteller/conformance/project-settings.json) the TypeScript SDK. Every rule refuses with a problem;
// nothing is dropped silently. Codes, colors, icons, date formats and time zones match in any ASCII case and are stored
// in the catalog's spelling; labels and plurals are trimmed of Unicode white space and measured in UTF-16 code units.
public static class ProjectSettingsRules
{
    // A key of Types that is not a type code, or two keys that differ only in case.
    public const string TypeCode = "type-code";

    // A label longer than the catalog's limit.
    public const string Label = "label";

    // A plural longer than the catalog's limit.
    public const string Plural = "plural";

    // A label or a plural that holds a control character (C0 or C1), a line or paragraph separator (U+2028, U+2029) or a
    // bidirectional control (U+061C, U+200E, U+200F, U+202A-U+202E, U+2066-U+2069): every client shows the names as text.
    public const string ControlCharacter = "control-character";

    // A plural of a type without a label.
    public const string PluralWithoutLabel = "plural-without-label";

    // Not one of the catalog's colors.
    public const string Color = "color";

    // Not one of the catalog's icons.
    public const string Icon = "icon";

    // Not one of the catalog's time zones.
    public const string TimeZone = "time-zone";

    // Not one of the catalog's date formats.
    public const string DateFormat = "date-format";

    // A member that a settings document or a type does not have.
    public const string UnknownMember = "unknown-member";

    // Two members that differ only in case.
    public const string CaseCollision = "case-collision";

    // A value of the wrong JSON type.
    public const string WrongType = "type";

    // A Format member that is not ProjectSettings.FileFormat.
    public const string Format = "format";

    internal const string TypesPath = "$.Types";
    internal const string DefaultTimeZonePath = "$.DefaultTimeZone";
    internal const string DateFormatPath = "$.DateFormat";

    private static readonly ProjectSettings Defaults = new();

    private delegate bool TryFind(string? value, out string? canonical);

    // Every rule id, as the conformance vectors spell them.
    public static IReadOnlyList<string> All { get; } =
    [
        TypeCode, Label, Plural, ControlCharacter, PluralWithoutLabel, Color, Icon, TimeZone, DateFormat, UnknownMember, CaseCollision, WrongType, Format,
    ];

    public static ProjectSettingsCheck Normalize(ProjectSettings input)
    {
        return Normalize(input, ProjectSettingsCatalog.Default);
    }

    // The settings with every value in its stored form (lower-case type codes in the catalog's order, trimmed names,
    // the catalog's spelling, blank values and empty types left out), or every problem. Version and Modified are kept.
    public static ProjectSettingsCheck Normalize(ProjectSettings input, ProjectSettingsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(catalog);

        var problems = new List<ProjectSettingsProblem>();
        var types = NormalizeTypes(input.Types, catalog, problems);
        var timeZone = NormalizeChoice(input.DefaultTimeZone, DefaultTimeZonePath, TimeZone, catalog.TryGetTimeZone, TimeZoneMessage, problems);
        var dateFormat = NormalizeChoice(input.DateFormat, DateFormatPath, DateFormat, catalog.TryGetDateFormat, value => DateFormatMessage(value, catalog), problems);

        if (problems.Count > 0)
        {
            return new ProjectSettingsCheck(null, problems);
        }

        return new ProjectSettingsCheck(
            input with { Types = types, DefaultTimeZone = timeZone, DateFormat = dateFormat },
            []);
    }

    // Whether two settings hold the same Types, DefaultTimeZone and DateFormat; Version and Modified do not count, type
    // codes match in any ASCII case and a type without any value is as no type. Null is the defaults (nothing stored).
    public static bool SameValues(ProjectSettings? left, ProjectSettings? right)
    {
        left ??= Defaults;
        right ??= Defaults;

        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (!string.Equals(left.DefaultTimeZone, right.DefaultTimeZone, StringComparison.Ordinal)
            || !string.Equals(left.DateFormat, right.DateFormat, StringComparison.Ordinal))
        {
            return false;
        }

        var leftTypes = ValuesOf(left.Types);
        var rightTypes = ValuesOf(right.Types);

        return leftTypes.Count == rightTypes.Count
            && leftTypes.All(entry => rightTypes.TryGetValue(entry.Key, out var other) && entry.Value == other);
    }

    private static Dictionary<string, ProjectTypeAppearance> NormalizeTypes(
        IReadOnlyDictionary<string, ProjectTypeAppearance>? input,
        ProjectSettingsCatalog catalog,
        List<ProjectSettingsProblem> problems)
    {
        var byCode = new Dictionary<string, ProjectTypeAppearance?>(StringComparer.Ordinal);
        var collisions = new List<string>();

        foreach (var (key, appearance) in input ?? new Dictionary<string, ProjectTypeAppearance>())
        {
            if (!catalog.TryGetTypeCode(key, out var code))
            {
                problems.Add(new ProjectSettingsProblem(ProjectSettingsText.Property(TypesPath, key), TypeCode, TypeCodeMessage(key, catalog)));
                continue;
            }

            if (!byCode.TryAdd(code, appearance) && !collisions.Contains(code))
            {
                collisions.Add(code);
            }
        }

        foreach (var code in collisions)
        {
            byCode.Remove(code);
            problems.Add(new ProjectSettingsProblem(
                ProjectSettingsText.Property(TypesPath, code),
                TypeCode,
                $"The type code {code} is given more than once, in different cases."));
        }

        var types = new Dictionary<string, ProjectTypeAppearance>(StringComparer.Ordinal);

        foreach (var code in catalog.TypeCodes)
        {
            if (byCode.TryGetValue(code, out var appearance)
                && appearance is not null
                && NormalizeAppearance(appearance, ProjectSettingsText.Property(TypesPath, code), catalog, problems) is { } normalized)
            {
                types.Add(code, normalized);
            }
        }

        return types;
    }

    private static ProjectTypeAppearance? NormalizeAppearance(
        ProjectTypeAppearance input,
        string path,
        ProjectSettingsCatalog catalog,
        List<ProjectSettingsProblem> problems)
    {
        var label = Trimmed(input.Label);
        var plural = Trimmed(input.Plural);

        if (label is not null && label.Length > catalog.LabelMaxLength)
        {
            problems.Add(new ProjectSettingsProblem(
                ProjectSettingsText.Property(path, nameof(ProjectTypeAppearance.Label)),
                Label,
                $"The label has {label.Length} characters; at most {catalog.LabelMaxLength} are allowed."));
        }

        if (plural is not null && plural.Length > catalog.PluralMaxLength)
        {
            problems.Add(new ProjectSettingsProblem(
                ProjectSettingsText.Property(path, nameof(ProjectTypeAppearance.Plural)),
                Plural,
                $"The plural has {plural.Length} characters; at most {catalog.PluralMaxLength} are allowed."));
        }

        AddControlCharacter(label, ProjectSettingsText.Property(path, nameof(ProjectTypeAppearance.Label)), "label", problems);
        AddControlCharacter(plural, ProjectSettingsText.Property(path, nameof(ProjectTypeAppearance.Plural)), "plural", problems);

        if (plural is not null && label is null)
        {
            problems.Add(new ProjectSettingsProblem(
                ProjectSettingsText.Property(path, nameof(ProjectTypeAppearance.Plural)),
                PluralWithoutLabel,
                "A plural needs a label; set the label too, or leave the plural out."));
        }

        string? color = null;

        if (input.Color is not null && !catalog.TryGetColor(input.Color, out color))
        {
            problems.Add(new ProjectSettingsProblem(
                ProjectSettingsText.Property(path, nameof(ProjectTypeAppearance.Color)),
                Color,
                $"'{input.Color}' is not a color of the catalog; use one of {string.Join(", ", catalog.Colors)}."));
        }

        string? icon = null;

        if (input.Icon is not null && !catalog.TryGetIcon(input.Icon, out icon))
        {
            problems.Add(new ProjectSettingsProblem(
                ProjectSettingsText.Property(path, nameof(ProjectTypeAppearance.Icon)),
                Icon,
                $"'{input.Icon}' is not an icon of the catalog, such as {catalog.Icons[0]} or i-lucide-building-2."));
        }

        if (label is null && plural is null && color is null && icon is null)
        {
            return null;
        }

        return new ProjectTypeAppearance { Label = label, Plural = plural, Color = color, Icon = icon };
    }

    // A blank value means none; any other must be a value of the catalog, in any ASCII case.
    private static string? NormalizeChoice(
        string? value,
        string path,
        string rule,
        TryFind find,
        Func<string, string> message,
        List<ProjectSettingsProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (find(value, out var canonical))
        {
            return canonical;
        }

        problems.Add(new ProjectSettingsProblem(path, rule, message(value)));
        return null;
    }

    // Checked after trimming, so a white space control at either end (a tab, a line break) is trimmed rather than refused.
    private static void AddControlCharacter(string? value, string path, string field, List<ProjectSettingsProblem> problems)
    {
        if (value is null)
        {
            return;
        }

        foreach (var character in value)
        {
            if (IsControlCharacter(character))
            {
                problems.Add(new ProjectSettingsProblem(
                    path,
                    ControlCharacter,
                    $"The {field} contains the character U+{(int)character:X4}; control characters, line and paragraph separators and bidirectional controls are not allowed."));
                return;
            }
        }
    }

    // char.IsControl is U+0000-U+001F and U+007F-U+009F.
    private static bool IsControlCharacter(char character)
    {
        var code = (int)character;
        return char.IsControl(character)
            || code is 0x2028 or 0x2029 or 0x200E or 0x200F or 0x061C
            || code is >= 0x202A and <= 0x202E
            || code is >= 0x2066 and <= 0x2069;
    }

    // string.Trim removes the characters of char.IsWhiteSpace, which are Unicode's White_Space.
    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static Dictionary<string, ProjectTypeAppearance> ValuesOf(IReadOnlyDictionary<string, ProjectTypeAppearance>? types)
    {
        var values = new Dictionary<string, ProjectTypeAppearance>(StringComparer.Ordinal);

        foreach (var (code, appearance) in types ?? new Dictionary<string, ProjectTypeAppearance>())
        {
            if (appearance is { Label: not null } or { Plural: not null } or { Color: not null } or { Icon: not null })
            {
                values[ProjectSettingsText.Fold(code)] = appearance;
            }
        }

        return values;
    }

    private static string TypeCodeMessage(string key, ProjectSettingsCatalog catalog)
    {
        return $"'{key}' is not a type code; the codes are {string.Join(", ", catalog.TypeCodes)}.";
    }

    private static string TimeZoneMessage(string value)
    {
        return $"'{value}' is not an IANA time zone id of the tz database, such as Europe/Prague or UTC.";
    }

    private static string DateFormatMessage(string value, ProjectSettingsCatalog catalog)
    {
        return $"'{value}' is not a date format; use one of {string.Join(", ", catalog.DateFormats)}.";
    }
}
