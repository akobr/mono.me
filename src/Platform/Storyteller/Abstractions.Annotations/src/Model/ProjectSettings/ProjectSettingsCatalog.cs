using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace _42.Platform.Storyteller;

// The allowed values of project settings: the catalog of src/Platform/Storyteller/conformance/project-settings.json, which
// this assembly embeds, so the API, sform, DbCreator and the SDKs share one list of type codes, colors, icons, date
// formats and time zones. A value matches in any ASCII case and comes back in the catalog's spelling.
public sealed class ProjectSettingsCatalog
{
    public const string ResourceName = "_42.Platform.Storyteller.project-settings.json";

    private static readonly Lazy<ProjectSettingsCatalog> Embedded = new(LoadEmbedded);

    private readonly Dictionary<string, string> _typeCodes;
    private readonly Dictionary<string, string> _colors;
    private readonly Dictionary<string, string> _icons;
    private readonly Dictionary<string, string> _dateFormats;
    private readonly Dictionary<string, string> _timeZones;
    private readonly Dictionary<string, int> _typeOrder;

    private ProjectSettingsCatalog(JsonElement catalog)
    {
        Format = ReadString(catalog, "format");
        TypeCodes = ReadList(catalog, "typeCodes");
        Colors = ReadList(catalog, "colors");
        Icons = ReadList(catalog, "icons");
        DateFormats = ReadList(catalog, "dateFormats");
        TimeZones = ReadList(catalog, "timeZones");

        _typeCodes = Index(TypeCodes, "typeCodes");
        _colors = Index(Colors, "colors");
        _icons = Index(Icons, "icons");
        _dateFormats = Index(DateFormats, "dateFormats");
        _timeZones = Index(TimeZones, "timeZones");

        var defaults = ReadObject(catalog, "defaults");
        DefaultTimeZone = ReadString(defaults, "timeZone");
        DefaultDateFormat = ReadString(defaults, "dateFormat");

        var limits = ReadObject(catalog, "limits");
        LabelMaxLength = ReadPositive(limits, "label");
        PluralMaxLength = ReadPositive(limits, "plural");

        var builtIn = ReadObject(catalog, "builtIn");
        var appearances = new Dictionary<string, ProjectTypeAppearance>(StringComparer.Ordinal);

        foreach (var code in TypeCodes)
        {
            var appearance = ReadObject(builtIn, code);
            var color = ReadString(appearance, "color");
            var icon = ReadString(appearance, "icon");

            Ensure(Colors.Contains(color, StringComparer.Ordinal), $"The built-in color {color} of {code} is not in the catalog.");
            Ensure(Icons.Contains(icon, StringComparer.Ordinal), $"The built-in icon {icon} of {code} is not in the catalog.");

            appearances.Add(code, new ProjectTypeAppearance
            {
                Label = ReadString(appearance, "label"),
                Plural = ReadString(appearance, "plural"),
                Color = color,
                Icon = icon,
            });
        }

        BuiltIn = appearances.AsReadOnly();
        _typeOrder = TypeCodes.Select((code, index) => (code, index)).ToDictionary(entry => entry.code, entry => entry.index, StringComparer.Ordinal);

        Ensure(Format == ProjectSettings.FileFormat, $"The catalog's format is {Format}, not {ProjectSettings.FileFormat}.");
        Ensure(_timeZones.TryGetValue(ProjectSettingsText.Fold(DefaultTimeZone), out var timeZone) && timeZone == DefaultTimeZone, $"The default time zone {DefaultTimeZone} is not in the catalog.");
        Ensure(_dateFormats.TryGetValue(ProjectSettingsText.Fold(DefaultDateFormat), out var dateFormat) && dateFormat == DefaultDateFormat, $"The default date format {DefaultDateFormat} is not in the catalog.");
    }

    // The catalog this assembly embeds, read once.
    public static ProjectSettingsCatalog Default => Embedded.Value;

    // ProjectSettings.FileFormat.
    public string Format { get; }

    // The seven type codes in the order clients show them and settings are written in.
    public IReadOnlyList<string> TypeCodes { get; }

    // The appearance of every type code when the project sets none; every member is set.
    public IReadOnlyDictionary<string, ProjectTypeAppearance> BuiltIn { get; }

    public IReadOnlyList<string> Colors { get; }

    public IReadOnlyList<string> Icons { get; }

    public IReadOnlyList<string> DateFormats { get; }

    // The IANA ids of the tz database, links, Etc zones and UTC included, in ordinal order.
    public IReadOnlyList<string> TimeZones { get; }

    public string DefaultTimeZone { get; }

    public string DefaultDateFormat { get; }

    // In UTF-16 code units, after trimming.
    public int LabelMaxLength { get; }

    public int PluralMaxLength { get; }

    // A project-settings.json document of the conformance suite, or any document with its catalog member.
    public static ProjectSettingsCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        return FromDocument(document);
    }

    public static ProjectSettingsCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var document = JsonDocument.Parse(stream);
        return FromDocument(document);
    }

    public bool TryGetTypeCode(string? value, [NotNullWhen(true)] out string? code) => Find(_typeCodes, value, out code);

    public bool TryGetColor(string? value, [NotNullWhen(true)] out string? color) => Find(_colors, value, out color);

    public bool TryGetIcon(string? value, [NotNullWhen(true)] out string? icon) => Find(_icons, value, out icon);

    public bool TryGetDateFormat(string? value, [NotNullWhen(true)] out string? dateFormat) => Find(_dateFormats, value, out dateFormat);

    public bool TryGetTimeZone(string? value, [NotNullWhen(true)] out string? timeZone) => Find(_timeZones, value, out timeZone);

    // The position of a type code in TypeCodes, or -1.
    public int IndexOfTypeCode(string code)
    {
        return TryGetTypeCode(code, out var canonical) ? _typeOrder[canonical] : -1;
    }

    private static ProjectSettingsCatalog FromDocument(JsonDocument document)
    {
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("catalog", out var catalog) || catalog.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The project settings catalog has no catalog member.");
        }

        return new ProjectSettingsCatalog(catalog);
    }

    private static ProjectSettingsCatalog LoadEmbedded()
    {
        using var stream = typeof(ProjectSettingsCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The project settings catalog {ResourceName} is not embedded.");

        return Load(stream);
    }

    private static bool Find(Dictionary<string, string> values, string? value, [NotNullWhen(true)] out string? canonical)
    {
        if (value is null)
        {
            canonical = null;
            return false;
        }

        return values.TryGetValue(ProjectSettingsText.Fold(value), out canonical);
    }

    private static Dictionary<string, string> Index(IReadOnlyList<string> values, string name)
    {
        var index = new Dictionary<string, string>(values.Count, StringComparer.Ordinal);

        foreach (var value in values)
        {
            Ensure(index.TryAdd(ProjectSettingsText.Fold(value), value), $"The catalog's {name} has {value} twice, in any case.");
        }

        return index;
    }

    private static JsonElement ReadObject(JsonElement parent, string name)
    {
        Ensure(parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object, $"The catalog's {name} is not an object.");
        return value;
    }

    private static string ReadString(JsonElement parent, string name)
    {
        Ensure(parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 0, $"The catalog's {name} is not a string.");
        return value.GetString()!;
    }

    private static int ReadPositive(JsonElement parent, string name)
    {
        Ensure(parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0, $"The catalog's {name} is not a positive integer.");
        return value.GetInt32();
    }

    private static IReadOnlyList<string> ReadList(JsonElement parent, string name)
    {
        Ensure(parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0, $"The catalog's {name} is not a list.");

        var list = new List<string>(value.GetArrayLength());

        foreach (var item in value.EnumerateArray())
        {
            Ensure(item.ValueKind == JsonValueKind.String && item.GetString()!.Length > 0, $"The catalog's {name} has an item that is not a string.");
            list.Add(item.GetString()!);
        }

        return list.AsReadOnly();
    }

    private static void Ensure([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new FormatException(message);
        }
    }
}
