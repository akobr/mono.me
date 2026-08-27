namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// A top-level math expression ('@{ ... }'). <see cref="Expression"/> is a <see cref="NumberNode"/>,
/// <see cref="BinaryNode"/>, or <see cref="StatementOperand"/>.
/// </summary>
public sealed class MathExpression : BindingNode
{
    public MathExpression(BindingNode expression)
    {
        Expression = expression;
    }

    public BindingNode Expression { get; }
}
