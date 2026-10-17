namespace _42.Platform.Storyteller.Transferring;

// The documents of the template of one annotation type that an export contains.
public sealed record class TemplateHistory
{
    // The lower-case type code.
    public required string AnnotationType { get; init; }

    // Oldest first; the current document is the last one.
    public required IReadOnlyList<ConfigurationDocument> Versions { get; init; }

    // Null when the template was deleted and only earlier versions remain.
    public ConfigurationDocument? Current { get; init; }
}
