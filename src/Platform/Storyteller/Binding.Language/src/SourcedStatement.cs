namespace _42.Platform.Storyteller.Binding.Language;

public sealed class SourcedStatement : Statement
{
    public SourcedStatement(PathNode path, string source)
    {
        Path = path;
        Source = source;
    }

    public PathNode Path { get; }

    public string Source { get; }
}
