namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// A top-level interpolation ('@[ ... ]'). Each part is a <see cref="TextPart"/> or a
/// <see cref="StatementPart"/>.
/// </summary>
public sealed class InterpolationExpression : BindingNode
{
    public InterpolationExpression(IReadOnlyList<BindingNode> parts)
    {
        Parts = parts;
    }

    public IReadOnlyList<BindingNode> Parts { get; }
}
