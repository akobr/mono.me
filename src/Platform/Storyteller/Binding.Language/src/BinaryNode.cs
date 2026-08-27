namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// A binary arithmetic operation. <see cref="Operator"/> is one of '+', '-', '*', '/', '%'.
/// </summary>
public sealed class BinaryNode : BindingNode
{
    public BinaryNode(char @operator, BindingNode left, BindingNode right)
    {
        Operator = @operator;
        Left = left;
        Right = right;
    }

    public char Operator { get; }

    public BindingNode Left { get; }

    public BindingNode Right { get; }
}
