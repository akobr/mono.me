namespace _42.tHolistic;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TestAttribute : Attribute
{
    public string? ExternalId { get; set; }

    public int Priority { get; set; }

    public string[]? Labels { get; set; }

    public Type? TestLifetime { get; set; }

    public Type? StepsLifetime { get; set; }

    public Type[]? Prerequisites { get; set; }

    public bool NonParallelizable { get; set; }

    public string[]? Dependencies { get; set; }

    public string[]? NonParallelizableWith { get; set; }
}
