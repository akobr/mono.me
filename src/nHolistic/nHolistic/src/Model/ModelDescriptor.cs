namespace _42.nHolistic;

public class ModelDescriptor : IModelDescriptor
{
    public string? Name { get; init; }

    public int? Priority { get; init; }

    public string[]? Dependencies { get; init; }

    public string[]? Labels { get; init; }
}
