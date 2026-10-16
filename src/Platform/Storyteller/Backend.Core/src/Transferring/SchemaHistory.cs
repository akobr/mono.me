namespace _42.Platform.Storyteller.Transferring;

// The documents of one schema that an export contains: a type schema, a descendant-type schema or an annotation
// schema.
public sealed record class SchemaHistory
{
    // TypeSchema, DescendantTypeSchema or AnnotationSchema.
    public required ViewDocumentKind Kind { get; init; }

    // The lower-case type code of a type schema or a descendant-type schema.
    public string? AnnotationType { get; init; }

    // The annotation of an annotation schema, or the ancestor of a descendant-type schema.
    public string? AnnotationKey { get; init; }

    // Oldest first; the current document is the last one.
    public required IReadOnlyList<ConfigurationDocument> Versions { get; init; }

    // Null when the schema was deleted and only earlier versions remain.
    public ConfigurationDocument? Current { get; init; }
}
