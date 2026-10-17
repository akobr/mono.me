using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Transferring;

// One document file of an uploaded archive: its document, or why it cannot be imported.
public sealed record class ViewArchiveFile
{
    public required string Path { get; init; }

    public required ViewDocumentKind Kind { get; init; }

    // The lower-case type code of a template, a type schema or a descendant-type schema.
    public string? AnnotationType { get; init; }

    // The key of an annotation, a configuration or an annotation schema, or the ancestor of a descendant-type schema.
    public AnnotationKey? AnnotationKey { get; init; }

    public JObject? Content { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }
}
