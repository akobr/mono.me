using System.Diagnostics.CodeAnalysis;

namespace _42.Crumble;

public class CrumbToGrainRegistry : ICrumbToGrainProvider, ICrumbToGrainRegister
{
    private readonly Dictionary<string, Type> _typesMap = new(StringComparer.Ordinal);

    public Type? GetCrumbTypeByKey(string crumbKey)
    {
        _typesMap.TryGetValue(crumbKey, out var type);
        return type;
    }

    public bool TryGetCrumbTypeByKey(string crumbKey, [MaybeNullWhen(false)]out Type type)
    {
        return _typesMap.TryGetValue(crumbKey, out type);
    }

    public void RegisterCrumb(string crumbKey, Type grainType)
    {
        _typesMap.TryAdd(crumbKey, grainType);
    }
}
