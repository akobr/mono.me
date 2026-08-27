namespace _42.Platform.Storyteller.Binding;

public interface IBindingFunction
{
    ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request);
}
