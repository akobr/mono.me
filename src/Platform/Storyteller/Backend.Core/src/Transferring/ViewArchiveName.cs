namespace _42.Platform.Storyteller.Transferring;

// The document a file name stands for (ViewArchiveNames.TryParse).
public sealed record class ViewArchiveName
{
    public required ViewDocumentKind Kind { get; init; }

    // The lower-case type code of a template, a type schema or a descendant-type schema.
    public string? AnnotationType { get; init; }

    // The key of an annotation, a configuration or an annotation schema, or the ancestor of a descendant-type schema.
    public AnnotationKey? AnnotationKey { get; init; }

    // An earlier version; null for the current document.
    public ulong? Version { get; init; }

    // Set for a document that cannot be imported whatever its content: a descendant type below a key it cannot
    // descend from (InvalidKey).
    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    public ViewArchiveContent Content => ViewArchiveNames.GetContent(Kind);

    // The same document in two files has the same identity, whatever the folders, the version and the spelling of
    // the key (encoded or plain).
    public string Identity => $"{Kind}:{AnnotationType}:{AnnotationKey}";
}
