namespace _42.Platform.Storyteller.Binding;

public sealed class BindingExample
{
    /// <summary>
    /// Gets the expression as it is stored in a configuration, for example <c>@config("/maxPrice")</c>.
    /// </summary>
    public required string Expression { get; init; }

    public required string Description { get; init; }
}
