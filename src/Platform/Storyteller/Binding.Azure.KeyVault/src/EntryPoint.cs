using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Binding;

public static class EntryPoint
{
    /// <summary>
    /// Adds Azure Key Vault bindings to the DI container. Expecting to have 'Key => Vault Uri' dictionary in section <c>AzureKeyVaults</c>.
    /// If only one KeyVault or default should be used, it must be configured with key <c>default</c>.
    /// </summary>
    public static IServiceCollection AddAzureKeyVaultBindings(
        this IServiceCollection @this,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        var vaults = configuration.GetSection("AzureKeyVaults");

        @this.AddAzureClients(builder =>
        {
            if (environment?.IsProduction() == true)
            {
                builder.UseCredential(new ChainedTokenCredential(
                    new ManagedIdentityCredential(),
                    new EnvironmentCredential()));
            }
            else
            {
                builder.UseCredential(new DefaultAzureCredential());
            }

            foreach (var vault in vaults.GetChildren())
            {
                builder.AddSecretClient(new Uri(vault.Value)).WithName(vault.Key);
            }
        });

        @this.Configure<BindingsOptions>(options =>
        {
            foreach (var vault in vaults.GetChildren())
            {
                options.AddStrategy(
                    provider => new KeyVaultBindingStrategy(
                        provider.GetRequiredService<IAzureClientFactory<SecretClient>>(), vault.Key),
                    vault.Key);
            }
        });

        return @this;
    }
}
