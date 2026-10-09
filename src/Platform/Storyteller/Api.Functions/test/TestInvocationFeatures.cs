using System.Collections;

using Microsoft.Azure.Functions.Worker;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class TestInvocationFeatures : IInvocationFeatures
{
    private readonly Dictionary<Type, object> _features = new();

    public void Set<T>(T instance)
    {
        _features[typeof(T)] = instance!;
    }

    // For worker features whose interface is internal and can't be named as a type argument.
    public void Set(Type featureType, object instance)
    {
        _features[featureType] = instance;
    }

    public T Get<T>()
    {
        return _features.TryGetValue(typeof(T), out var feature) ? (T)feature : default!;
    }

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        return _features.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
