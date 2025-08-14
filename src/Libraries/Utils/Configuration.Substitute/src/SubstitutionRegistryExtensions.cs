using System;

namespace _42.Utils.Configuration.Substitute;

public static class SubstitutionRegistryExtensions
{
    public static ISubstitutionRegistry AddFunction(this ISubstitutionRegistry @this, Func<string, string?> substitute, string? sourceKey = null)
    {
        @this.RegisterSubstitution(
            sourceKey,
            new FunctionSubstituteStrategy(substitute));

        return @this;
    }
}
