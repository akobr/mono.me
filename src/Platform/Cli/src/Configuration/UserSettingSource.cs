namespace _42.Platform.Cli.Configuration;

public enum UserSettingSource
{
    // Not set: the commands use the default.
    Default,

    // sform.settings.json.
    Settings,

    // editor.config.json or access.default.json of an earlier version, next to the assembly.
    Legacy,

    // $VISUAL or $EDITOR for the editor, STORYTELLER_PROJECT or STORYTELLER_VIEW for the default project and view.
    Environment,

    // A machine sign-in (STORYTELLER_* credentials) targets the project of its credentials, never the default project.
    Machine,
}
