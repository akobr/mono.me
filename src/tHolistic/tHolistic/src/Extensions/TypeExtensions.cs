using System.Reflection;

namespace _42.tHolistic;

public static class TypeExtensions
{
    public static bool IsTypeWithSteps(this Type @this)
    {
        if (!@this.IsClass || @this.IsAbstract)
        {
            return false;
        }

        var stepsAttribute = @this.GetCustomAttribute<StepsAttribute>();

        if (stepsAttribute is not null)
        {
            return true;
        }

        var stepMethods = @this
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        var hasSteps = stepMethods.Any(method => method.GetCustomAttribute<StepAttribute>() is not null);
        return hasSteps;
    }

    public static bool IsFixture(this Type @this)
    {
        return @this is { IsClass: true, IsAbstract: false }
               && @this.GetInterface(nameof(IFixture)) is not null;
    }

    public static Type GetFixtureType(this Type @this)
    {
        if (!@this.IsFixture())
        {
            throw new ArgumentException("Type is not a fixture.", nameof(@this));
        }

        var fixtureType = GetGenericArgumentOfGenericInterface(@this, typeof(IFixture<>));
        return fixtureType ?? @this;
    }

    public static Type? GetGenericArgumentOfGenericInterface(this Type @this, Type interfaceDefinitionType)
    {
        if (!interfaceDefinitionType.IsGenericTypeDefinition)
        {
            throw new ArgumentException("Type is not a definition of generic interface.", nameof(interfaceDefinitionType));
        }

        var interfaceFullType = @this.GetInterfaces().FirstOrDefault(
            i => i.IsGenericType && i.GetGenericTypeDefinition() == interfaceDefinitionType);
        return interfaceFullType?.GenericTypeArguments[0];
    }

    public static string GetFixtureTargetLabel(this Type @this)
    {
        var labelAttribute = @this.GetCustomAttribute<LabelAttribute>();

        if (labelAttribute is not null)
        {
            return labelAttribute.Label;
        }

        return @this.FullName ?? @this.Name;
    }
}
