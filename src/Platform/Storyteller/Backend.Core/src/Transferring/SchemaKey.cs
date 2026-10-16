namespace _42.Platform.Storyteller.Transferring;

// The identity of a schema in a view: a type schema has a type code, an annotation schema a key, and a
// descendant-type schema both (the type below the key). The type code is lower-case and the key canonical
// (AnnotationKey.ToString()), so equal schemas have equal keys.
public readonly record struct SchemaKey(ViewDocumentKind Kind, string? AnnotationType, string? AnnotationKey)
{
    public static SchemaKey ForType(string annotationType) => new(ViewDocumentKind.TypeSchema, annotationType, null);

    public static SchemaKey ForAnnotation(string annotationKey) => new(ViewDocumentKind.AnnotationSchema, null, annotationKey);

    public static SchemaKey ForDescendantType(string annotationKey, string annotationType) => new(ViewDocumentKind.DescendantTypeSchema, annotationType, annotationKey);
}
