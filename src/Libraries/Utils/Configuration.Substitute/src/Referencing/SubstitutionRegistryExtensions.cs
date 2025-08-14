using Microsoft.Extensions.Configuration;

namespace _42.Utils.Configuration.Substitute.Referencing;

public static class SubstitutionRegistryExtensions
{
    public static ISubstitutionRegistry AddReferencing(this ISubstitutionRegistry @this, IConfiguration configuration, string? sourceKey = null)
    {
        @this.RegisterSubstitution(
            sourceKey,
            new ReferenceSubstituteStrategy(configuration));

        return @this;
    }
}
