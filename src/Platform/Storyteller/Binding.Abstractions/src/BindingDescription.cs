namespace _42.Platform.Storyteller.Binding;

public sealed class BindingDescription
{
    /// <summary>
    /// Gets one sentence about what the binding resolves to. Markdown is allowed.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Gets the call shape of a function, for example <c>config("&lt;expr&gt;")</c>.
    /// </summary>
    public string? Signature { get; init; }

    public IReadOnlyList<BindingParameterDescription> Parameters { get; init; } = [];

    public IReadOnlyList<BindingExample> Examples { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether a source holds secrets, resolved only for callers allowed to read them.
    /// </summary>
    public bool IsSecret { get; init; }
}
