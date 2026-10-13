namespace _42.Platform.Storyteller.Binding;

public sealed class BindingParameterDescription
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public bool IsOptional { get; init; }

    /// <summary>
    /// Gets the allowed values when the set is closed, otherwise empty.
    /// </summary>
    public IReadOnlyList<string> Values { get; init; } = [];
}
