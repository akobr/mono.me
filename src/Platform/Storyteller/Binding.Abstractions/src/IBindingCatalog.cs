namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// Lists the registered binding functions and sources with their descriptions. It never resolves a value.
/// </summary>
public interface IBindingCatalog
{
    BindingCatalog GetCatalog();
}
