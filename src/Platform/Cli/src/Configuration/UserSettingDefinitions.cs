using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Configuration;

// Every preference of sform account settings. A new preference is one entry here and its properties in UserSettingsSchema;
// the list, set and unset commands need no change.
public static class UserSettingDefinitions
{
    public const string EDITOR_TYPE = "editor.type";
    public const string EDITOR_COMMAND = "editor.command";
    public const string ACCESS_PROJECT = "access.project";
    public const string ACCESS_VIEW = "access.view";

    // The project of a user without a default project, as CommandContext picks it.
    private const string DefaultProjectKey = $"{Platform.Storyteller.Constants.DefaultTenantName}.{Platform.Storyteller.Constants.DefaultProjectName}";

    private static readonly IReadOnlyList<(string Name, EditorType Type)> EditorNames =
    [
        ("vscode", EditorType.VsCode),
        ("neovim", EditorType.Neovim),
        ("vim", EditorType.Vim),
        ("custom", EditorType.Custom),
    ];

    public static IReadOnlyList<string> EditorTypeNames { get; } = EditorNames.Select(editor => editor.Name).ToList();

    public static IReadOnlyList<UserSettingDefinition> All { get; } =
    [
        new()
        {
            Key = EDITOR_TYPE,
            Section = ConfigurationSections.EDITOR,
            Properties = [UserSettingsSchema.EDITOR_TYPE],
            Description = "The editor of config edit, config patch, template edit and the schema edit commands.",
            ValueFormat = string.Join(", ", EditorTypeNames),
            AllowedValues = EditorTypeNames,
            DefaultDisplay = "(ask on first use)",
            Parse = value => ParseEditorName(value) is { } type
                ? Changes(UserSettingsSchema.EDITOR_TYPE, type.ToString())
                : null,
            Read = section => UserSettingsSchema.ParseEditorType(UserSettingsSchema.GetValue(section, UserSettingsSchema.EDITOR_TYPE)) is { } type
                ? ToEditorName(type)
                : null,
        },
        new()
        {
            Key = EDITOR_COMMAND,
            Section = ConfigurationSections.EDITOR,
            Properties = [UserSettingsSchema.CUSTOM_COMMAND],
            Description = "The command line of a custom editor; sform appends the file path. It should wait for the editor to close, e.g. code --wait.",
            ValueFormat = "a command line, e.g. \"code --wait\"",
            DefaultDisplay = "(not set)",
            Parse = value => CustomEditorCommand.IsValid(value) ? Changes(UserSettingsSchema.CUSTOM_COMMAND, value.Trim()) : null,
            Read = section => UserSettingsSchema.ParseText(UserSettingsSchema.GetValue(section, UserSettingsSchema.CUSTOM_COMMAND)),
        },
        new()
        {
            Key = ACCESS_PROJECT,
            Section = ConfigurationSections.ACCESS,
            Properties = [UserSettingsSchema.ORGANIZATION_NAME, UserSettingsSchema.PROJECT_NAME],
            Description = "The default project of the commands without -p, as sform account set sets it.",
            ValueFormat = "<organization>.<project>",
            DefaultValue = DefaultProjectKey,
            DefaultDisplay = DefaultProjectKey,
            Parse = value => TrySplitProjectKey(value, out var organization, out var project)
                ? new Dictionary<string, JToken?>
                {
                    [UserSettingsSchema.ORGANIZATION_NAME] = organization,
                    [UserSettingsSchema.PROJECT_NAME] = project,
                }
                : null,
            Read = ReadProjectKey,
        },
        new()
        {
            Key = ACCESS_VIEW,
            Section = ConfigurationSections.ACCESS,
            Properties = [UserSettingsSchema.VIEW_NAME],
            Description = "The default view of the commands without -v.",
            ValueFormat = "a view name",
            DefaultValue = Platform.Storyteller.Constants.DefaultViewName,
            DefaultDisplay = Platform.Storyteller.Constants.DefaultViewName,
            Parse = value => string.IsNullOrWhiteSpace(value) ? null : Changes(UserSettingsSchema.VIEW_NAME, value.Trim()),
            Read = section => UserSettingsSchema.ParseText(UserSettingsSchema.GetValue(section, UserSettingsSchema.VIEW_NAME)),
        },
    ];

    public static UserSettingDefinition? Find(string? key)
    {
        return All.FirstOrDefault(definition => string.Equals(definition.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // vscode, neovim, vim or custom, in any case.
    public static EditorType? ParseEditorName(string? name)
    {
        var match = EditorNames.FirstOrDefault(editor => string.Equals(editor.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match.Name is null ? null : match.Type;
    }

    public static string ToEditorName(EditorType type)
    {
        return EditorNames.First(editor => editor.Type == type).Name;
    }

    // <organization>.<project>, split at the first dot as sform account set splits it; both parts are needed.
    public static bool TrySplitProjectKey(string? projectKey, out string organization, out string project)
    {
        var key = projectKey?.Trim() ?? string.Empty;
        var separator = key.IndexOf('.');

        organization = separator > 0 ? key[..separator].Trim() : string.Empty;
        project = separator > 0 ? key[(separator + 1)..].Trim() : string.Empty;
        return organization.Length > 0 && project.Length > 0;
    }

    private static string? ReadProjectKey(JObject section)
    {
        var organization = UserSettingsSchema.ParseText(UserSettingsSchema.GetValue(section, UserSettingsSchema.ORGANIZATION_NAME));
        var project = UserSettingsSchema.ParseText(UserSettingsSchema.GetValue(section, UserSettingsSchema.PROJECT_NAME));

        // A missing half is the default one, as CommandContext fills it in.
        return organization is null && project is null
            ? null
            : $"{organization ?? Platform.Storyteller.Constants.DefaultTenantName}.{project ?? Platform.Storyteller.Constants.DefaultProjectName}";
    }

    private static Dictionary<string, JToken?> Changes(string property, string value)
    {
        return new Dictionary<string, JToken?> { [property] = value };
    }
}
