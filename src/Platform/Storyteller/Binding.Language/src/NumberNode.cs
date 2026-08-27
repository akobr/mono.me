namespace _42.Platform.Storyteller.Binding.Language;

public sealed class NumberNode : BindingNode
{
    public NumberNode(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }
}
