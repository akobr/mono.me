namespace _42.Platform.Storyteller.Binding;

public class BindingsOptions
{
    private readonly Dictionary<string, Func<IServiceProvider, IBindingStrategy>> _registrations = new();

    public BindingsOptions AddStrategy(
        Func<IServiceProvider, IBindingStrategy> registration,
        string key = BindingService.DefaultBindingKey)
    {
        _registrations[key] = registration;
        return this;
    }

    internal IEnumerable<KeyValuePair<string, IBindingStrategy>> Resolve(IServiceProvider provider)
    {
        return _registrations.Select(registration
            => new KeyValuePair<string, IBindingStrategy>(
                registration.Key,
                registration.Value(provider)));
    }
}
