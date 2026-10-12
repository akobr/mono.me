using System;

namespace _42.Platform.Storyteller;

// Content types with a meaning for the platform. Any other value is a hint for clients only.
public static class KeyValueContentTypes
{
    // A public pair whose value points to a secret in the customer's own vault; never resolved by the platform.
    public const string SecretReference = "application/vnd.2s.secret-reference";

    // The Key Vault reference format of Azure App Configuration, accepted as is.
    public const string AppConfigurationKeyVaultReference = "application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8";

    public static bool IsReference(string? contentType)
    {
        return contentType is not null
            && (contentType.Equals(SecretReference, StringComparison.OrdinalIgnoreCase)
                || contentType.Equals(AppConfigurationKeyVaultReference, StringComparison.OrdinalIgnoreCase));
    }
}
