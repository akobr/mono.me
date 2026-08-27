namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// A function call statement. Each argument is a <see cref="Statement"/>, <see cref="PathNode"/>, or
/// <see cref="StringLiteralNode"/>.
/// </summary>
public sealed class FunctionStatement : Statement
{
    public FunctionStatement(string name, IReadOnlyList<BindingNode> arguments)
    {
        Name = name;
        Arguments = arguments;
    }

    public string Name { get; }

    public IReadOnlyList<BindingNode> Arguments { get; }
}
