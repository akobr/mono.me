namespace _42.Platform.Storyteller.Api.Models;

// The body of a merge patch of an annotation (RFC 7396), for the document only: the handler reads the JSON itself, so
// it can tell a member left out (kept) from a null one (cleared). Every member is optional, and nullable but UnitType
// and UnitDefinition (AnnotationDocumentFilter); Values merge name by name, a null value removes its name.
public sealed class AnnotationMergePatch
{
    public bool? IsDisabled { get; set; }

    public DateTimeOffset? ValidFrom { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public string? TimeZone { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? DocumentationLink { get; set; }

    public IReadOnlyList<string>? Labels { get; set; }

    public IReadOnlyDictionary<string, object>? Values { get; set; }

    // A unit's; it cannot be cleared, so it is not nullable in the document.
    public string? UnitType { get; set; }

    public string? UnitDefinition { get; set; }
}
