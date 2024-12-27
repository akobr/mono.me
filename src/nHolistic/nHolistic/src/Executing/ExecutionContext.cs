namespace _42.nHolistic;

public class ExecutionContext
{
    public List<ExecutionBatch> Batches { get; } = new();

    public List<ExecutionStage> Stages { get; } = new();
}
