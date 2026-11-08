using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Configuration;

// The sections of sform.settings.json and their known properties. A section is read the same way for the configuration,
// for the list of sform account settings and for the migration of the legacy files: only the known properties, matched
// in any case and named in camelCase, with trimmed non-empty text, and the editor type by its enum name.
public static class UserSettingsSchema
{
    public const string ORGANIZATION_NAME = "organizationName";
    public const string PROJECT_NAME = "projectName";
    public const string VIEW_NAME = "viewName";
    public const string EDITOR_TYPE = "editorType";
    public const string CUSTOM_COMMAND = "customCommand";

    private static readonly Dictionary<string, IReadOnlyList<string>> Properties = new(StringComparer.OrdinalIgnoreCase)
    {
        [ConfigurationSections.ACCESS] = [ORGANIZATION_NAME, PROJECT_NAME, VIEW_NAME],
        [ConfigurationSections.EDITOR] = [EDITOR_TYPE, CUSTOM_COMMAND],
    };

    public static IReadOnlyList<string> SectionNames { get; } = [ConfigurationSections.ACCESS, ConfigurationSections.EDITOR];

    public static IReadOnlyList<string> GetProperties(string section)
    {
        return Properties[section];
    }

    // The known properties of a section, normalised; unknown properties and invalid values are left out.
    public static JObject Normalize(string section, JObject raw)
    {
        var normalized = new JObject();

        foreach (var name in Properties[section])
        {
            var value = GetValue(raw, name);
            var text = name == EDITOR_TYPE
                ? ParseEditorType(value)?.ToString()
                : ParseText(value);

            if (text is not null)
            {
                normalized[name] = text;
            }
        }

        return normalized;
    }

    // A property in any case of its name, as the configuration binds it.
    public static JToken? GetValue(JObject section, string name)
    {
        return section.GetValue(name, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ParseText(JToken? value)
    {
        if (value is not JValue { Value: not null } scalar)
        {
            return null;
        }

        var text = Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    // An enum name in any case, or the number of a defined value (the old editor.config.json wrote numbers).
    public static EditorType? ParseEditorType(JToken? value)
    {
        var text = ParseText(value);

        if (text is null || text.Contains(','))
        {
            return null;
        }

        return Enum.TryParse<EditorType>(text, ignoreCase: true, out var type) && Enum.IsDefined(type)
            ? type
            : null;
    }

    public static EditorOptions ToEditorOptions(JObject section)
    {
        return new EditorOptions
        {
            EditorType = ParseEditorType(GetValue(section, EDITOR_TYPE)),
            CustomCommand = ParseText(GetValue(section, CUSTOM_COMMAND)),
        };
    }

    // The editor section of a choice: the type, and the command of a custom editor (removed for the others).
    public static IReadOnlyDictionary<string, JToken?> EditorChanges(EditorOptions options)
    {
        return new Dictionary<string, JToken?>
        {
            [EDITOR_TYPE] = options.EditorType is { } type ? new JValue(type.ToString()) : null,
            [CUSTOM_COMMAND] = options.EditorType == EditorType.Custom ? Text(options.CustomCommand) : null,
        };
    }

    // The default project and view, as sform account set, register and access-point create save them.
    public static IReadOnlyDictionary<string, JToken?> AccessChanges(string? organizationName, string? projectName, string? viewName)
    {
        return new Dictionary<string, JToken?>
        {
            [ORGANIZATION_NAME] = Text(organizationName),
            [PROJECT_NAME] = Text(projectName),
            [VIEW_NAME] = Text(viewName),
        };
    }

    // The normalised section that a set of changes leaves, for comparing it with the stored one.
    public static JObject Apply(string section, JObject current, IReadOnlyDictionary<string, JToken?> changes)
    {
        var result = (JObject)current.DeepClone();

        foreach (var (name, value) in changes)
        {
            foreach (var property in result.Properties().Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                property.Remove();
            }

            if (value is not null && value.Type != JTokenType.Null)
            {
                result[name] = value.DeepClone();
            }
        }

        return Normalize(section, result);
    }

    private static JValue? Text(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : new JValue(value.Trim());
    }
}
