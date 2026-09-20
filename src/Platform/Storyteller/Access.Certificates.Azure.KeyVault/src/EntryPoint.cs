using _42.Platform.Storyteller.Accessing;
using Azure.Security.KeyVault.Keys;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.Platform.Storyteller;

public static class KeyVaultCertificateAuthorityEntryPoint
{
    public static IServiceCollection AddKeyVaultCertificateAuthority(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var kvSection = configuration.GetSection($"{MachineAuthenticationOptions.SectionName}:Authority:KeyVault");
        services.Configure<KeyVaultCertificateAuthorityOptions>(kvSection);

        var kvOptions = kvSection.Get<KeyVaultCertificateAuthorityOptions>() ?? new();
        var vaultUri = configuration.GetSection("AzureKeyVaults")[kvOptions.VaultName];

        if (vaultUri is not null)
        {
            services.AddAzureClients(builder =>
            {
                builder.AddKeyClient(new Uri(vaultUri)).WithName(kvOptions.VaultName);
            });
        }

        // Replace the local CA provider with the Key Vault one.
        services.Replace(ServiceDescriptor.Singleton<ICertificateAuthorityProvider, KeyVaultCertificateAuthorityProvider>());

        return services;
    }
}
