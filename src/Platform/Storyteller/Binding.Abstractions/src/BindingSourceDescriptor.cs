namespace _42.Platform.Storyteller.Binding;

public sealed class BindingSourceDescriptor
{
    /// <summary>
    /// Gets the registered key, used as <c>@(path, key)</c>.
    /// </summary>
    public required string Name { get; init; }

    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the source is registered as <c>default</c>, so <c>@path.to.value</c> reads it.
    /// </summary>
    public bool IsDefault { get; init; }

    public bool IsSecret { get; init; }

    public IReadOnlyList<BindingExample> Examples { get; init; } = [];
}
