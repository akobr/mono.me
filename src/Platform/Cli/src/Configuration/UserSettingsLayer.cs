namespace _42.Platform.Cli.Configuration;

public enum UserSettingsLayer
{
    // Neither the settings file nor a legacy file has the section.
    None,

    // sform.settings.json has the section, an empty one too.
    Settings,

    // Only the legacy file of the section (editor.config.json, access.default.json) has it.
    Legacy,
}
