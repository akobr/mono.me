namespace _42.Platform.Storyteller.Binding.Language;

public sealed class PathNode : BindingNode
{
    public PathNode(IReadOnlyList<string> segments)
    {
        Segments = segments;
    }

    public IReadOnlyList<string> Segments { get; }
}
