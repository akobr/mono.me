namespace _42.Platform.Storyteller.Binding;

public interface IBindingSource
{
    ValueTask<BindingValue?> ResolveAsync(BindingRequest request);
}
