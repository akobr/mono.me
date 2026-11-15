using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using _42.Platform.Storyteller;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Appearance = _42.Platform.Storyteller.ProjectTypeAppearance;
using SdkAppearance = _42.Platform.Storyteller.Sdk.ProjectTypeAppearance;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;
using Settings = _42.Platform.Storyteller.ProjectSettings;

namespace _42.Platform.Cli.Commands.Projects;

// The project settings between the SDK (what the API answers and takes), the shared model and rules of
// Abstractions.Annotations (ProjectSettingsJson, ProjectSettingsRules) and the documents sform shows, edits and writes:
// the file shape of ProjectSettingsJson, as Newtonsoft documents for the editor and the diff. The SDK names clash with
// the model's (ProjectSettings, ProjectTypeAppearance), so both go by aliases.
internal static class ProjectSettingsDocuments
{
    private const string IconPrefix = "i-";
    private const string LucidePrefix = "i-lucide-";

    // The shared model of an answer of the API, as it is: no rule is applied, so settings of a newer catalog still show.
    public static Settings ToModel(SdkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var types = new Dictionary<string, Appearance>(StringComparer.Ordinal);

        foreach (var (code, appearance) in settings.Types ?? new Dictionary<string, SdkAppearance>())
        {
            if (appearance is null)
            {
                continue;
            }

            types[code] = new Appearance
            {
                Label = appearance.Label,
                Plural = appearance.Plural,
                Color = appearance.Color,
                Icon = appearance.Icon,
            };
        }

        return new Settings
        {
            Types = types,
            DefaultTimeZone = settings.DefaultTimeZone,
            DateFormat = settings.DateFormat,
            Version = settings.Version,
        };
    }

    // The body of a PUT: the values only (the API ignores Version and Modified).
    public static SdkSettings ToSdk(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SdkSettings
        {
            Types = settings.Types.ToDictionary(
                entry => entry.Key,
                entry => new SdkAppearance
                {
                    Label = entry.Value.Label,
                    Plural = entry.Value.Plural,
                    Color = entry.Value.Color,
                    Icon = entry.Value.Icon,
                },
                StringComparer.Ordinal),
            DefaultTimeZone = settings.DefaultTimeZone,
            DateFormat = settings.DateFormat,
        };
    }

    // The file shape (the Format marker, Types, DefaultTimeZone, DateFormat): what edit opens, -e writes and the diffs show.
    public static JObject ToFileDocument(Settings settings)
    {
        return ToNewtonsoft(ProjectSettingsJson.ToJson(settings, ProjectSettingsJsonShape.File));
    }

    public static string ToText(Settings settings)
    {
        return ToFileDocument(settings).ToString(Formatting.Indented);
    }

    // A System.Text.Json object as a Newtonsoft one; a string that looks like a date stays the string it is.
    public static JObject ToNewtonsoft(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var reader = new JsonTextReader(new StringReader(json.ToJsonString())) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    // A Newtonsoft document as System.Text.Json reads it, with its nulls.
    public static JsonNode? ToNode(JToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return JsonNode.Parse(token.ToString(Formatting.None));
    }

    // The ETag of a version, as the API sends it ("<Version>"): for If-Match when an answer has no header.
    public static string ETagOf(long version)
    {
        return $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";
    }

    // An icon as the catalog names it: building-2 is i-lucide-building-2; a name that starts with i- stays as it is.
    public static string ToIconName(string icon)
    {
        var trimmed = icon.Trim();

        return trimmed.Length == 0 || trimmed.StartsWith(IconPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : LucidePrefix + trimmed;
    }

    // A type code or a built-in name of a type, in any case, as the type code of the catalog: sbt, Subject, subject;
    // unit of execution, Unit-of-execution and UnitOfExecution (the API's type name). A project's label never is one.
    public static bool TryGetTypeCode(string? value, out string code)
    {
        code = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var catalog = ProjectSettingsCatalog.Default;

        if (catalog.TryGetTypeCode(value.Trim(), out var known))
        {
            code = known;
            return true;
        }

        var name = Compact(value);

        foreach (var typeCode in catalog.TypeCodes)
        {
            if (string.Equals(Compact(catalog.BuiltIn[typeCode].Label!), name, StringComparison.OrdinalIgnoreCase))
            {
                code = typeCode;
                return true;
            }
        }

        return false;
    }

    // The type codes with their built-in names, for the message of a --type that is neither.
    public static string DescribeTypes()
    {
        var catalog = ProjectSettingsCatalog.Default;
        return string.Join(", ", catalog.TypeCodes.Select(code => $"{code} ({catalog.BuiltIn[code].Label})"));
    }

    // An English plural of a type name, as the admin UI derives one when a project gives a label without a plural:
    // Job → Jobs, Policy → Policies, Batch → Batches.
    public static string Pluralize(string name)
    {
        if (name.Length >= 2 && (name[^1] is 'y' or 'Y') && !"aeiouAEIOU".Contains(name[^2], StringComparison.Ordinal))
        {
            return $"{name[..^1]}ies";
        }

        var lower = name.ToLowerInvariant();

        return lower.EndsWith('s') || lower.EndsWith('x') || lower.EndsWith('z') || lower.EndsWith("ch", StringComparison.Ordinal) || lower.EndsWith("sh", StringComparison.Ordinal)
            ? $"{name}es"
            : $"{name}s";
    }

    private static string Compact(string value)
    {
        return string.Concat(value.Where(character => !char.IsWhiteSpace(character) && character is not ('-' or '_')));
    }
}
