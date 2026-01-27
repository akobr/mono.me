using System.Reflection;

namespace _42.Crumble;

public static class MethodInfoExtensions
{
    // TODO: [P1] This needs o be adapted to use hash
    public static string GetCrumbKey(this MethodInfo @this)
    {
        ArgumentNullException.ThrowIfNull(@this);

        if (@this is { DeclaringType: null })
        {
            throw new ArgumentException(
                "The delegate must be a valid method which represent a crumb, has [Crumb] attribute.",
                nameof(@this));
        }

        var parameters = @this.GetParameters();
        var nameSuffix = @this.IsGenericMethod
            ? $"~{@this.GetGenericArguments().Length}"
            : string.Empty;
        var argumentList = parameters.Length > 0
            ? string.Join(", ", parameters.Select(p => p.ParameterType.FullName))
            : string.Empty;

        var crumbKey = $"{@this.DeclaringType!.FullName}.{@this.Name}{nameSuffix}({argumentList})";
        return crumbKey;
    }

}
