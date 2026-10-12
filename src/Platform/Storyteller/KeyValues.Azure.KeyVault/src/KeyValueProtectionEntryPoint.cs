using _42.Platform.Storyteller.KeyValues.Protection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues;

public static class KeyValueProtectionEntryPoint
{
    /// <summary>
    /// Adds the Key Vault key-encryption providers of the key/value protection levels. The Standard level needs the
    /// vault <c>AzureKeyVaults:{KeyValues:Protection:Standard:VaultName}</c>; without it only public pairs work.
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

        var vaultUri = configuration.GetSection("AzureKeyVaults")[protection.Standard.VaultName];

        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            return @this;
        }

        var client = KeyVaultClients.CreateKeyClient(new Uri(vaultUri), protection.Emulator, isProduction);
        @this.AddSingleton<IKeyEncryptionProvider>(provider => new KeyVaultStandardKeyProvider(
            client,
            provider.GetRequiredService<IOptions<KeyValueOptions>>(),
            provider.GetService<ILogger<KeyVaultStandardKeyProvider>>()));

        return @this;
    }
}
