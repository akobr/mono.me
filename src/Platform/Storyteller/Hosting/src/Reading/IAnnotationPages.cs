namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// What the polling informer reads: pages of the two lists of a responsibility, and point reads. The catalog's reader
// answers over HTTP; tests may answer without it.
internal interface IAnnotationPages
{
    // One page of the active executions or usages of the responsibility, from a continuation token; null asks for the
    // first page. Throws StorytellerConfigurationException.
    Task<AnnotationPage> ReadPageAsync(FullKey responsibility, AnnotationList list, string? continuationToken, CancellationToken cancellationToken);

    // In any status; includeDeleted: deleted ones too. Throws StorytellerConfigurationException, with 404 and no error
    // code for an annotation that is missing (CatalogReader.IsMissing).
    Task<StorytellerAnnotation> ReadAnnotationAsync(FullKey key, bool includeDeleted, CancellationToken cancellationToken);
}
