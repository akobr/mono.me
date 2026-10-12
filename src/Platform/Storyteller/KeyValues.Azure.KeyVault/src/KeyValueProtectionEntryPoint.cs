using _42.Platform.Storyteller.KeyValues.Protection;
using Azure.Security.KeyVault.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues;

public static class KeyValueProtectionEntryPoint
{
    /// <summary>
    /// Adds the Key Vault key-encryption providers of the key/value protection levels:
    /// Standard needs the vault <c>AzureKeyVaults:{KeyValues:Protection:Standard:VaultName}</c>, Hsm the vault
    /// <c>AzureKeyVaults:{KeyValues:Protection:Hsm:VaultName}</c> or a Managed HSM per organization, and Byok the
    /// cross-tenant application <c>KeyValues:Protection:Byok:ClientId</c>. Without them only public pairs work.
    /// </summary>
    public static IServiceCollection AddKeyVaultKeyProtection(
        this IServiceCollection @this,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        @this.Configure<KeyValueOptions>(configuration.GetSection(KeyValueOptions.SectionName));

        var protection = configuration.GetSection($"{KeyValueOptions.SectionName}:Protection").Get<KeyValueProtectionOptions>() ?? new();
        var isProduction = environment?.IsProduction() == true;

        if (protection.Emulator && isProduction)
        {
            throw new InvalidOperationException("KeyValues:Protection:Emulator must not be set in production.");
        }

        var vaults = configuration.GetSection("AzureKeyVaults");
        var standardUri = vaults[protection.Standard.VaultName];

        if (!string.IsNullOrWhiteSpace(standardUri))
        {
            var client = KeyVaultClients.CreateKeyClient(new Uri(standardUri), protection.Emulator, isProduction);
            @this.AddSingleton<IKeyEncryptionProvider>(provider => KeyVaultKeyProvider.CreateStandard(
                client,
                provider.GetRequiredService<IOptions<KeyValueOptions>>(),
                provider.GetService<ILogger<KeyVaultKeyProvider>>()));
        }

        var premiumUri = vaults[protection.Hsm.VaultName];
        var managedHsms = protection.OrganizationManagedHsms
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(
                entry => entry.Key,
                entry => KeyVaultClients.CreateKeyClient(new Uri(entry.Value), protection.Emulator, isProduction),
                StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(premiumUri) || managedHsms.Count > 0)
        {
            var premiumClient = string.IsNullOrWhiteSpace(premiumUri)
                ? null
                : KeyVaultClients.CreateKeyClient(new Uri(premiumUri), protection.Emulator, isProduction);
            @this.AddSingleton<IKeyEncryptionProvider>(provider => KeyVaultKeyProvider.CreateHsm(
                premiumClient,
                managedHsms,
                provider.GetRequiredService<IOptions<KeyValueOptions>>(),
                provider.GetService<ILogger<KeyVaultKeyProvider>>()));
        }

        if (protection.Byok.IsConfigured || protection.Emulator)
        {
            @this.AddSingleton<CustomerKeyProvider>();
            @this.AddSingleton<IKeyEncryptionProvider>(provider => provider.GetRequiredService<CustomerKeyProvider>());
            @this.AddSingleton<ICustomerKeyProvider>(provider => provider.GetRequiredService<CustomerKeyProvider>());
        }

        return @this;
    }
}
