using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    // Keycloak confidential clients for ClientCredentials machine access, with any user provider.
    // Needs the Keycloak section (server, realm, audience, admin credentials; checked at startup).
    public static IServiceCollection AddKeycloakMachineAccess(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.EnsureNoOtherIdentityProviderMachineAccess("Keycloak");
        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection(KeycloakOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KeycloakOptions>, KeycloakOptionsValidator>());
        services.AddHttpClient();

        services.AddSingleton<KeycloakMachineAccessService>();
        services.AddSingleton<IIdentityProviderMachineAccessService>(provider => provider.GetRequiredService<KeycloakMachineAccessService>());
        services.AddSingleton<IMachineTokenValidator, KeycloakMachineTokenValidator>();
        return services;
    }

    // Keycloak users of the realm Auth:Keycloak:Issuer, for Auth:Provider Keycloak (Auth:Keycloak, checked at startup).
    // Independent of the Keycloak section of machine access, which may name the same realm.
    public static IServiceCollection AddKeycloakUserAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddUserAuthenticationOptions(configuration);
        services.TryAddSingleton<KeycloakClaimNormalizer>();
        services.AddSingleton<IBearerClaimsNormalizer>(provider => provider.GetRequiredService<KeycloakClaimNormalizer>());

        // One instance (one realm metadata manager) for API routes and the subject tokens of the user token exchange.
        services.TryAddSingleton<KeycloakBearerTokenValidator>();
        services.AddSingleton<IBearerTokenValidator>(provider => provider.GetRequiredService<KeycloakBearerTokenValidator>());
        services.TryAddSingleton<IUserSubjectTokenValidator, KeycloakUserSubjectTokenValidator>();
        return services;
    }
}
