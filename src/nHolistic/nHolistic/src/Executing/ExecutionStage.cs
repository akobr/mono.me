namespace _42.nHolistic;

public class ExecutionStage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public List<ExecutionBatch> Batches { get; init; } = new();
}
