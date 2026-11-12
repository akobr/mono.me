using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// A bulk writer that creates every annotation it gets, with the ancestors it is told to report created, and reports
// them all committed in one plan (not in a dry run). It records each call with the dry run's active keys at its start.
internal sealed class FakeBulkWriter : IAnnotationBulkWriter
{
    public List<(IReadOnlyList<Annotation> Annotations, AnnotationBulkOptions Options, IReadOnlySet<string>? DryRunActiveAtStart)> Calls { get; } = [];

    public IReadOnlyList<string> AncestorsCreated { get; set; } = ["rst.made-on-the-way"];

    public Task<AnnotationBulkResult> CreateMissingAnnotationsAsync(
        string organization,
        string project,
        string view,
        IReadOnlyList<Annotation> annotations,
        AuditActor actor,
        AnnotationBulkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((annotations, options!, options?.DryRunActiveKeys?.ToHashSet(StringComparer.Ordinal)));

        if (options?.DryRun == false)
        {
            options.Progress?.Report(annotations.Count + AncestorsCreated.Count);
        }

        return Task.FromResult(new AnnotationBulkResult(
            annotations.Select(annotation => new AnnotationBulkItem(AnnotationKey.Parse(annotation.AnnotationKey), new ViewImportResult(ViewImportOutcome.Created))).ToList(),
            AncestorsCreated,
            2));
    }
}
