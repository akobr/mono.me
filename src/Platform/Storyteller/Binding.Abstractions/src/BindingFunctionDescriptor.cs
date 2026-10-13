namespace _42.Platform.Storyteller.Binding;

public sealed class BindingFunctionDescriptor
{
    /// <summary>
    /// Gets the registered name, called as <c>@name(…)</c>.
    /// </summary>
    public required string Name { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string? Signature { get; init; }

    public IReadOnlyList<BindingParameterDescription> Parameters { get; init; } = [];

    public IReadOnlyList<BindingExample> Examples { get; init; } = [];
}
