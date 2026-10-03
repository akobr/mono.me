using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller;

public static class AuthKitEntryPoint
{
    public static IServiceCollection AddAuthKitUserAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddUserAuthenticationOptions(configuration);
        services.AddSingleton<AuthKitClaimNormalizer>();
        services.AddSingleton<IBearerClaimsNormalizer>(provider => provider.GetRequiredService<AuthKitClaimNormalizer>());
        services.AddSingleton<IBearerTokenValidator, AuthKitBearerTokenValidator>();

        // Typed clients are transient, so the resolver is too.
        services.AddHttpClient<WorkOsManagementClient>();
        services.AddTransient<IUserProfileResolver, AuthKitUserProfileResolver>();
        return services;
    }
}
