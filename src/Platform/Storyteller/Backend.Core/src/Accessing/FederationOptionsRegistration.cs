using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

public static class FederationOptionsRegistration
{
    // Binds MachineAuth:Federation; an empty Issuer or Audience is derived from MachineAuth:TrustDomain,
    // which AddCertificateMachineAccess binds (its default applies without it). Checked at startup.
    public static OptionsBuilder<FederationOptions> AddFederationOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FederationOptions>, FederationOptionsValidator>());
        return services.AddOptions<FederationOptions>()
            .Bind(configuration.GetSection(FederationOptions.SectionName))
            .PostConfigure<IOptions<MachineAuthenticationOptions>>(
                (options, machineOptions) => options.ApplyTrustDomainDefaults(machineOptions.Value.TrustDomain))
            .ValidateOnStart();
    }
}
