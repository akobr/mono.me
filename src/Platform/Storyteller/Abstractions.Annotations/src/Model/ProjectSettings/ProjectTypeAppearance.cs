namespace _42.Platform.Storyteller;

// How a project shows one annotation type; a missing value falls back to the built-in one of the catalog. Only the
// presentation changes: the type codes, the keys and the API type names stay.
public sealed record class ProjectTypeAppearance
{
    // The singular name, e.g. "Company".
    public string? Label { get; init; }

    // Only with Label; clients derive it from the label when it is missing.
    public string? Plural { get; init; }

    // A color of the catalog, e.g. "pink".
    public string? Color { get; init; }

    // An icon of the catalog, e.g. "i-lucide-building-2".
    public string? Icon { get; init; }
}
