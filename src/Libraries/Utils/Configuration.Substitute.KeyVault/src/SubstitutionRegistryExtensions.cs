using System;
using Azure.Security.KeyVault.Secrets;

namespace _42.Utils.Configuration.Substitute.KeyVault;

public static class SubstitutionRegistryExtensions
{
    public static ISubstitutionRegistry AddKeyVault(this ISubstitutionRegistry @this, Func<SecretClient> secretClientFactory, string? sourceKey = null)
    {
        @this.RegisterSubstitution(
            sourceKey,
            new KeyVaultSubstituteStrategy(secretClientFactory()));

        return @this;
    }
}
