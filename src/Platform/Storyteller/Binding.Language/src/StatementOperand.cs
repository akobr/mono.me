namespace _42.Platform.Storyteller.Binding.Language;

public sealed class StatementOperand : BindingNode
{
    public StatementOperand(Statement statement)
    {
        Statement = statement;
    }

    public Statement Statement { get; }
}
