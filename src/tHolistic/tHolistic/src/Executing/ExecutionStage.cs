namespace _42.tHolistic;

public class ExecutionStage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public List<ExecutionBatch> Batches { get; init; } = new();
}
