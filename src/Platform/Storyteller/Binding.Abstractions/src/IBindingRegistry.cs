namespace _42.Platform.Storyteller.Binding;

public interface IBindingRegistry
{
    void RegisterSource(string key, IBindingSource source);

    void RegisterFunction(string name, IBindingFunction function);
}
