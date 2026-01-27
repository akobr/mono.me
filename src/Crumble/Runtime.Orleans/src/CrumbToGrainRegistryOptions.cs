namespace _42.Crumble;

public class CrumbToGrainRegistryOptions : ICrumbToGrainRegister
{
    internal Dictionary<string, Type> Types { get; } = new(StringComparer.Ordinal);

    public ICrumbToGrainRegister RegisterCrumb(string crumbKey, Type grainType)
    {
        Types.TryAdd(crumbKey, grainType);
        return this;
    }
}
