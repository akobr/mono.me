using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// A bulk writer that creates every annotation it gets, with the ancestors it is told to report created, and reports
// them all committed in one plan (not in a dry run). It records each call with the dry run's active keys at its start,
// and each completion of the relation arrays, which reports the parents it is told to.
internal sealed class FakeBulkWriter : IAnnotationBulkWriter
{
    public List<(IReadOnlyList<Annotation> Annotations, AnnotationBulkOptions Options, IReadOnlySet<string>? DryRunActiveAtStart)> Calls { get; } = [];

    public List<(IReadOnlyList<string> Keys, AnnotationBulkOptions Options)> Completions { get; } = [];

    public IReadOnlyList<string> AncestorsCreated { get; set; } = ["rst.made-on-the-way"];

    public int RelationsToComplete { get; set; }

    // Called at each completion, before it returns, so a test can see what ran before it.
    public Action? OnComplete { get; set; }

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

    public Task<int> CompleteRelationsAsync(
        string organization,
        string project,
        string view,
        IReadOnlyCollection<AnnotationKey> keys,
        AnnotationBulkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Completions.Add((keys.Select(key => key.ToString()).ToList(), options!));
        OnComplete?.Invoke();
        return Task.FromResult(RelationsToComplete);
    }
}
