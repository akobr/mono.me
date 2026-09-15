using _42.Platform.Storyteller.Binding.Language;

namespace _42.Platform.Storyteller.Binding;

public class BindingsOptions
{
    private readonly Dictionary<string, Func<IServiceProvider, IBindingSource>> _sources = new();
    private readonly Dictionary<string, Func<IServiceProvider, IBindingFunction>> _functions = new();

    public BindingsOptions AddSource(
        Func<IServiceProvider, IBindingSource> registration,
        string key = BindingExecutor.DefaultSourceKey)
    {
        _sources[key] = registration;
        return this;
    }

    public BindingsOptions AddFunction(
        string name,
        Func<IServiceProvider, IBindingFunction> registration)
    {
        _functions[name] = registration;
        return this;
    }

    internal IEnumerable<KeyValuePair<string, IBindingSource>> ResolveSources(IServiceProvider provider)
    {
        return _sources.Select(registration
            => new KeyValuePair<string, IBindingSource>(registration.Key, registration.Value(provider)));
    }

    internal IEnumerable<KeyValuePair<string, IBindingFunction>> ResolveFunctions(IServiceProvider provider)
    {
        return _functions.Select(registration
            => new KeyValuePair<string, IBindingFunction>(registration.Key, registration.Value(provider)));
    }
}
