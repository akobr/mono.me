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
}
