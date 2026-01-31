using System.Reflection;
using System.Text;

namespace _42.Crumble;

public static class MethodInfoExtensions
{
    public static string GetCrumbKey(this MethodInfo @this)
    {
        ArgumentNullException.ThrowIfNull(@this);

        if (@this is { DeclaringType: null })
        {
            throw new ArgumentException(
                "The delegate must be a valid method which represent a crumb, has [Crumb] attribute.",
                nameof(@this));
        }

        var crumbAttribute = @this.GetCustomAttribute<CrumbAttribute>();

        if (crumbAttribute is null)
        {
            throw new ArgumentException(
                "The delegate must be a valid method which represent a crumb, has [Crumb] attribute.",
                nameof(@this));
        }

        var crumbKey = !string.IsNullOrWhiteSpace(crumbAttribute.Key)
            ? crumbAttribute.Key
            : CalculateCrumbKey(@this);

        return crumbKey;
    }

    private static string CalculateCrumbKey(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var nameSuffix = method.IsGenericMethod
            ? $"~{method.GetGenericArguments().Length}"
            : string.Empty;
        var argumentList = parameters.Length > 0
            ? string.Join(",", parameters.Select(p => p.ParameterType.FullName))
            : string.Empty;

        var crumbFullName = $"{method.DeclaringType!.FullName}.{method.Name}{nameSuffix}({argumentList})";
        var crumbFullNameHash = MurmurHash3.Hash32(Encoding.UTF8.GetBytes(crumbFullName));
        var crumbKey = $"{method.DeclaringType!.FullName}.{method.Name}.{crumbFullNameHash:x2}";
        return crumbKey;
    }
}
