using System.Collections.Generic;
using System.Collections.Immutable;

namespace _42.Platform.Storyteller;

// The settings of a project that every client shows: the names, colors and icons of its annotation types, the time zone
// new annotations start in, and the date format. Stored on the project's access point, read by every member and machine
// of the project, changed by its Administrators. The allowed values are those of ProjectSettingsCatalog;
// ProjectSettingsRules and ProjectSettingsJson validate and normalize a document of any client.
public sealed record class ProjectSettings
{
    // The marker of the file shape: a data set's project-settings.json and the export of sform project settings.
    public const string FileFormat = "2s.project-settings.v1";

    // Keyed by lower-case type code (rst, unt, sbt, usg, cnt, exe, uxe); a missing type uses the built-in appearance.
    public IReadOnlyDictionary<string, ProjectTypeAppearance> Types { get; init; } = ImmutableDictionary<string, ProjectTypeAppearance>.Empty;

    // An id of the catalog's time zones; null for UTC.
    public string? DefaultTimeZone { get; init; }

    // One of the catalog's date formats; null for yyyy-mm-dd.
    public string? DateFormat { get; init; }

    // Raised by every change; 0 when the settings were never set.
    public long Version { get; init; }

    // The last change; null when the settings were never set.
    public AuditStamp? Modified { get; init; }
}
