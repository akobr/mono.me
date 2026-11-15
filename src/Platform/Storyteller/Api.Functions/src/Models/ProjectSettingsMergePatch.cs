namespace _42.Platform.Storyteller.Api.Models;

// The body of a merge patch of project settings (RFC 7396), for the document only: the handler reads the JSON itself
// (ProjectSettingsJson.ApplyMergePatch), so it can tell a member left out (kept) from a null one (removed). Every member
// is optional and nullable, also those of a type (ProjectSettingsDocumentFilter); a null type goes back to its built-in
// appearance.
public sealed class ProjectSettingsMergePatch
{
    public IReadOnlyDictionary<string, ProjectTypeAppearance>? Types { get; set; }

    public string? DefaultTimeZone { get; set; }

    public string? DateFormat { get; set; }
}
