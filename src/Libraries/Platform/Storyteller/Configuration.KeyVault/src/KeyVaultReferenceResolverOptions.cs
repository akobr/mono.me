using Azure.Security.KeyVault.Secrets;

namespace _42.Platform.Storyteller.Sdk.Configuring.KeyVault;

// The vaults a KeyVaultReferenceResolver may read. Copied when the resolver is created, so a later change does nothing.
public sealed class KeyVaultReferenceResolverOptions
{
    // The suffix of the public Azure cloud.
    public const string AzureVaultSuffix = "vault.azure.net";

    // The suffix a kv://{vault}/… shorthand expands to, as on the server: https://{vault}.{suffix}/. Always allowed.
    public string DefaultVaultSuffix { get; set; } = AzureVaultSuffix;

    // The hosts a secret URI may name: a host equal to a suffix, or ending with '.' and a suffix, compared without case
    // on the IDN host. Sovereign clouds and the host of a local emulator are added here. A secret URI has no port, so an
    // emulator on another port gets it back in SecretClientFactory.
    public IList<string> AllowedHostSuffixes { get; } = [AzureVaultSuffix];

    // Empty admits any vault. When set, every other vault is declined. The vault of a secret URI is the first label of
    // its host, so myvault admits both kv://myvault/x and https://myvault.vault.azure.net/secrets/x.
    public IList<string> AllowedVaults { get; } = [];

    // Passed to every SecretClient the resolver creates.
    public SecretClientOptions? SecretClientOptions { get; set; }

    // Creates the client of a vault from its URI, https://{host}/, instead of the default factory: for tests and custom
    // clients. Called once per vault.
    public Func<Uri, SecretClient>? SecretClientFactory { get; set; }
}
