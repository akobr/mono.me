using System.Diagnostics.CodeAnalysis;

namespace _42.Crumble;

public interface ICrumbToGrainProvider
{
    Type? GetCrumbTypeByKey(string crumbKey);

    bool TryGetCrumbTypeByKey(string crumbKey, [MaybeNullWhen(false)] out Type type);
}
