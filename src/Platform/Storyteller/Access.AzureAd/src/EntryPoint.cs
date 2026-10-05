using _42.Platform.Storyteller.Accessing;

using Azure.Identity;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    // Entra ID app registrations for ClientCredentials machine access, with any user provider.
    // Needs MachineAuth:AzureAd:TenantId, Auth:TenantId, Auth:ClientId and Auth:AppRoles (checked at startup).
    public static IServiceCollection AddAzureAdMachineAccess(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.EnsureNoOtherIdentityProviderMachineAccess("Entra ID");
        services.AddUserAuthenticationOptions(configuration);
        services.AddOptions<AzureAdMachineAccessOptions>()
            .Bind(configuration.GetSection(AzureAdMachineAccessOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UserAuthenticationOptions>, AzureAdMachineAccessOptionsValidator>());

        // Graph calls run as the Function's identity (DefaultAzureCredential) in the machine directory.
        services.TryAddSingleton(provider => new GraphServiceClient(new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            TenantId = provider.GetRequiredService<IOptions<AzureAdMachineAccessOptions>>().Value.TenantId,
        })));
        services.TryAddSingleton<EntraIdBearerTokenValidator>();

        services.AddSingleton<AzureAdMachineAccessService>();
        services.AddSingleton<IIdentityProviderMachineAccessService>(provider => provider.GetRequiredService<AzureAdMachineAccessService>());
        services.AddSingleton<IMachineTokenValidator, EntraIdMachineTokenValidator>();
        return services;
    }

    public static IServiceCollection AddEntraIdUserAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddUserAuthenticationOptions(configuration);
        services.AddSingleton<IBearerClaimsNormalizer, EntraIdClaimNormalizer>();

        // One instance (one OpenID configuration manager) when Entra ID machine access is added too.
        services.TryAddSingleton<EntraIdBearerTokenValidator>();
        services.AddSingleton<IBearerTokenValidator>(provider => provider.GetRequiredService<EntraIdBearerTokenValidator>());
        return services;
    }
}
