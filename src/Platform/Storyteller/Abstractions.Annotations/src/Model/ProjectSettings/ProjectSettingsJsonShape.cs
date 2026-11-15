namespace _42.Platform.Storyteller;

// The JSON shapes of project settings, both PascalCase with the type codes in the catalog's order.
public enum ProjectSettingsJsonShape
{
    // The answer of the API: Types, DefaultTimeZone, DateFormat, Version and Modified.
    Api = 0,

    // A file (a data set's project-settings.json, the export of sform project settings): the Format marker, Types,
    // DefaultTimeZone and DateFormat, without Version and Modified.
    File,
}
