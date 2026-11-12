namespace _42.Platform.Storyteller.Annotating;

public sealed record class AnnotationBulkOptions
{
    // Decide every outcome without writing.
    public bool DryRun { get; init; }

    // The transactional batches that run at once; the database's throughput is shared with the API.
    public int MaxParallelBatches { get; init; } = 4;

    // The memory bound of one executed plan: the serialized items of at most this many annotations are held at once.
    public int MaxUnitsPerPlan { get; init; } = 2_000;

    // The annotations committed so far, reported after each executed plan.
    public IProgress<int>? Progress { get; init; }

    // In a dry run, the keys that earlier steps of the same dry run made active without storing them (the active keys of
    // an import's overlay). An ancestor among them counts as active, as an annotation import counts the overlay: it is
    // not created with defaults, and a parent among them counts as a relation update. A real run reads what the earlier
    // steps wrote and ignores them.
    public IReadOnlySet<string>? DryRunActiveKeys { get; init; }
}
