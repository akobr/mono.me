namespace _42.Platform.Storyteller.Binding;

public sealed class BindingFunctionRequest
{
    public required string Name { get; init; }

    public required IReadOnlyList<BindingValue> Arguments { get; init; }

    public required bool IncludeSecrets { get; init; }
}
