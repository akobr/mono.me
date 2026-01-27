using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace _42.Crumble;

public class CrumbToGrainRegistry(IOptions<CrumbToGrainRegistryOptions> options)
    : ICrumbToGrainProvider, ICrumbToGrainRegister
{
    private readonly CrumbToGrainRegistryOptions _registry = options.Value;

    public Type? GetCrumbTypeByKey(string crumbKey)
    {
        _registry.Types.TryGetValue(crumbKey, out var type);
        return type;
    }

    public bool TryGetCrumbTypeByKey(string crumbKey, [MaybeNullWhen(false)]out Type type)
    {
        return _registry.Types.TryGetValue(crumbKey, out type);
    }

    public ICrumbToGrainRegister RegisterCrumb(string crumbKey, Type grainType)
    {
        _registry.Types.TryAdd(crumbKey, grainType);
        return this;
    }
}
