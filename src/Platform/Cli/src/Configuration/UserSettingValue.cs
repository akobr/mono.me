using System.Collections.Generic;
using System.Linq;
using _42.Platform.Cli.Authentication;

namespace _42.Platform.Cli.Configuration;

// The value of a preference that the commands use, and where it comes from. Value is null when it is not set; the
// commands then use Definition.DefaultValue. Variable names the environment variable of the source Environment; Note
// says why a stored value is not used.
public sealed record UserSettingValue(UserSettingDefinition Definition, string? Value, UserSettingSource Source, string? Variable = null, string? Note = null)
{
    public string Display => Source == UserSettingSource.Machine ? "(of the machine sign-in)" : Value ?? Definition.DefaultDisplay;

    public string? EffectiveValue => Source == UserSettingSource.Machine ? null : Value ?? Definition.DefaultValue;

    // Every preference of the catalog, resolved as the commands resolve it: the editor through EditorResolution, so a
    // missing editor shows $VISUAL or $EDITOR when the edit commands would use it, and the default project and view as
    // CommandContext picks them, after STORYTELLER_PROJECT and STORYTELLER_VIEW, and never for a machine sign-in.
    public static IReadOnlyList<UserSettingValue> Resolve(UserSettingsSnapshot snapshot, EnvironmentEditor environment, MachineSignInSettings target)
    {
        var editorSection = snapshot[ConfigurationSections.EDITOR];
        var configuredEditor = UserSettingsSchema.ToEditorOptions(editorSection.Values);
        var editor = EditorResolution.Resolve(configuredEditor, environment);
        var hasTargetProject = MachineSignInSettings.TrySplitProject(target.Project, out _, out _);

        return UserSettingDefinitions.All.Select(definition => definition.Key switch
        {
            UserSettingDefinitions.EDITOR_TYPE when editor.Origin == EditorOrigin.Environment =>
                new UserSettingValue(definition, UserSettingDefinitions.ToEditorName(EditorType.Custom), UserSettingSource.Environment, environment.Variable),
            UserSettingDefinitions.EDITOR_COMMAND when editor.Origin == EditorOrigin.Environment =>
                new UserSettingValue(definition, environment.Command, UserSettingSource.Environment, environment.Variable),
            UserSettingDefinitions.ACCESS_PROJECT when hasTargetProject =>
                new UserSettingValue(definition, target.Project, UserSettingSource.Environment, TargetVariables.PROJECT),
            UserSettingDefinitions.ACCESS_VIEW when target.View is not null =>
                new UserSettingValue(definition, target.View, UserSettingSource.Environment, TargetVariables.VIEW),
            UserSettingDefinitions.ACCESS_PROJECT or UserSettingDefinitions.ACCESS_VIEW when target.IsConfigured =>
                new UserSettingValue(definition, null, UserSettingSource.Machine, Note: string.Join(", ", target.CredentialVariables)),

            // The view of the settings belongs to their project: another project from STORYTELLER_PROJECT gets the default view.
            UserSettingDefinitions.ACCESS_VIEW when hasTargetProject =>
                new UserSettingValue(definition, null, UserSettingSource.Default, Note: $"{TargetVariables.PROJECT} is set"),
            UserSettingDefinitions.EDITOR_COMMAND when configuredEditor is { EditorType: not (null or EditorType.Custom), CustomCommand: not null } =>
                Stored(snapshot, definition) with { Note = "used by a custom editor only" },
            _ => Stored(snapshot, definition),
        }).ToList();
    }

    private static UserSettingValue Stored(UserSettingsSnapshot snapshot, UserSettingDefinition definition)
    {
        var section = snapshot[definition.Section];
        var stored = definition.Read(section.Values);
        var source = (stored, section.Layer) switch
        {
            (null, _) => UserSettingSource.Default,
            (_, UserSettingsLayer.Legacy) => UserSettingSource.Legacy,
            _ => UserSettingSource.Settings,
        };

        return new UserSettingValue(definition, stored, source);
    }
}
