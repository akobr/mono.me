namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// What the polling informers read: pages of the two lists of a responsibility, pages of the descendants of any
// annotation, and point reads. The catalog's reader answers over HTTP; tests may answer without it.
internal interface IAnnotationPages
{
    // One page of the active executions or usages of the responsibility, from a continuation token; null asks for the
    // first page. Throws StorytellerConfigurationException.
    Task<AnnotationPage> ReadPageAsync(FullKey responsibility, AnnotationList list, string? continuationToken, CancellationToken cancellationToken);

    // One page of the descendants of a type under the ancestor, …/annotations/{ancestor}/{segment}, active ones only
    // unless includeInactive; never deleted ones. Throws as ReadPageAsync does.
    Task<AnnotationPage> ReadDescendantsPageAsync(FullKey ancestor, AnnotationType descendants, bool includeInactive, string? continuationToken, CancellationToken cancellationToken);

    // In any status; includeDeleted: deleted ones too. Throws StorytellerConfigurationException, with 404 and no error
    // code for an annotation that is missing (CatalogReader.IsMissing).
    Task<StorytellerAnnotation> ReadAnnotationAsync(FullKey key, bool includeDeleted, CancellationToken cancellationToken);
}
