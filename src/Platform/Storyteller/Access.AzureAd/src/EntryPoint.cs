using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    public static IServiceCollection AddAzureAdMachineAccess(
        this IServiceCollection services)
    {
        services.AddSingleton<IMachineAccessService, AzureAdMachineAccessService>();
        return services;
    }

    public static IServiceCollection AddEntraIdUserAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddUserAuthenticationOptions(configuration);
        services.AddSingleton<IBearerClaimsNormalizer, EntraIdClaimNormalizer>();
        services.AddSingleton<IBearerTokenValidator, EntraIdBearerTokenValidator>();
        return services;
    }
}
