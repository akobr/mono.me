namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// A binding source or function that describes itself for the binding catalog (<see cref="IBindingCatalog"/>).
/// Optional: a binding without it is listed by its name only.
/// </summary>
public interface IDescribedBinding
{
    /// <param name="name">The name the binding is registered under: a source key or a function name.</param>
    BindingDescription Describe(string name);
}
