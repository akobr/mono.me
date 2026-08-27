namespace _42.Platform.Storyteller.Binding.Language;

public sealed class PathStatement : Statement
{
    public PathStatement(PathNode path)
    {
        Path = path;
    }

    public PathNode Path { get; }
}
