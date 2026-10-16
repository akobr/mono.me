namespace _42.Platform.Storyteller.Transferring;

// The schema a file of an import names: a type schema has a type code, an annotation schema a key, and a
// descendant-type schema both (the type below the key). Kind is TypeSchema, DescendantTypeSchema or AnnotationSchema.
public sealed record class ViewSchemaTarget(
    string Organization,
    string Project,
    string View,
    ViewDocumentKind Kind,
    string? AnnotationType,
    string? AnnotationKey);
