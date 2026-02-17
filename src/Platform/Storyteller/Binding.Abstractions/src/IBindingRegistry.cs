namespace _42.Platform.Storyteller.Binding;

public interface IBindingRegistry
{
    void RegisterStrategy(string key, IBindingStrategy strategy);
}
