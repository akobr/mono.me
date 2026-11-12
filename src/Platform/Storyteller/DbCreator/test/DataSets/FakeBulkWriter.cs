using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// A bulk writer that creates every annotation it gets, with one ancestor it reports created.
internal sealed class FakeBulkWriter : IAnnotationBulkWriter
{
    public List<(IReadOnlyList<Annotation> Annotations, AnnotationBulkOptions Options)> Calls { get; } = [];

    public Task<AnnotationBulkResult> CreateMissingAnnotationsAsync(
        string organization,
        string project,
        string view,
        IReadOnlyList<Annotation> annotations,
        AuditActor actor,
        AnnotationBulkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((annotations, options!));

        return Task.FromResult(new AnnotationBulkResult(
            annotations.Select(annotation => new AnnotationBulkItem(AnnotationKey.Parse(annotation.AnnotationKey), new ViewImportResult(ViewImportOutcome.Created))).ToList(),
            ["rst.made-on-the-way"],
            2));
    }
}
