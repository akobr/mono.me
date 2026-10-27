namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// One page of a list as the SDK read it. Items serve keys and fingerprints only: their values are lossy. A page may
// hold no item and still have a continuation token; null ends the list.
internal sealed record AnnotationPage(IReadOnlyList<Annotation> Items, string? ContinuationToken);
