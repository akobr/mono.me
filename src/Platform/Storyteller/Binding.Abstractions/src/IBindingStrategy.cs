namespace _42.Platform.Storyteller.Binding;

public interface IBindingStrategy
{
    ValueTask<bool> TryBinding(BindingContext context);
}
