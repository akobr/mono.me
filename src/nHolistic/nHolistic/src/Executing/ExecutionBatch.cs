namespace _42.tHolistic;

public class ExecutionBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required IReadOnlyCollection<TestCaseContext> TestCases { get; init; }

    public bool IsNonParallelizable { get; init; }

    public HashSet<Guid> NonParallelizableWith { get; init; } = new();

    public HashSet<Guid> Dependencies { get; init; } = new();
}
