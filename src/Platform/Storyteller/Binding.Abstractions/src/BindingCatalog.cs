namespace _42.Platform.Storyteller.Binding;

public sealed class BindingCatalog
{
    public IReadOnlyList<BindingFunctionDescriptor> Functions { get; init; } = [];

    public IReadOnlyList<BindingSourceDescriptor> Sources { get; init; } = [];
}
