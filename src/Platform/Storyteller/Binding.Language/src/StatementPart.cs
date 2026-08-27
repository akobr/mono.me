namespace _42.Platform.Storyteller.Binding.Language;

public sealed class StatementPart : BindingNode
{
    public StatementPart(Statement statement)
    {
        Statement = statement;
    }

    public Statement Statement { get; }
}
