namespace _42.Platform.Storyteller.Binding.Language;

public sealed class StringLiteralNode : BindingNode
{
    public StringLiteralNode(string value)
    {
        Value = value;
    }

    public string Value { get; }
}
