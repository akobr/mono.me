using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace _42.Platform.Storyteller;

public static class CertificateEntryPoint
{
    public static IServiceCollection AddCertificateMachineAccess(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Ordering dependency: AddApiKeyMachineAccess() must be called first.
        if (services.All(sd => sd.ImplementationType != typeof(ApiKeyMachineAccessService)
                             && sd.ImplementationFactory?.Method.ReturnType != typeof(ApiKeyMachineAccessService)))
        {
            var hasApiKeyService = services.Any(sd =>
                sd.ServiceType == typeof(ApiKeyMachineAccessService));

            if (!hasApiKeyService)
            {
                throw new InvalidOperationException(
                    "AddApiKeyMachineAccess() must be called before AddCertificateMachineAccess(). " +
                    "The certificate entry point depends on ApiKeyMachineAccessService being registered.");
            }
        }

        services.Configure<MachineAuthenticationOptions>(
            configuration.GetSection(MachineAuthenticationOptions.SectionName));

        services.AddSingleton<CertificateMachineAccessService>();
        services.AddSingleton<LocalCertificateAuthority>();
        services.AddSingleton<IClientCertificateAuthority>(p => p.GetRequiredService<LocalCertificateAuthority>());
        services.AddSingleton<ClientCertificateValidator>();
        services.AddSingleton<IClientCertificateValidator>(p => p.GetRequiredService<ClientCertificateValidator>());
        services.AddSingleton<PolicyAwareMachineAccessService>();

        // Replace the IMachineAccessService registration with the policy-aware one.
        services.Replace(ServiceDescriptor.Singleton<IMachineAccessService>(
            p => p.GetRequiredService<PolicyAwareMachineAccessService>()));

        // Ensure IApiKeyValidator is explicitly registered backed by ApiKeyMachineAccessService.
        services.TryAddSingleton<IApiKeyValidator>(
            p => p.GetRequiredService<ApiKeyMachineAccessService>());

        // Register the local CA provider by default (can be overridden by AddKeyVaultCertificateAuthority).
        services.TryAddSingleton<ICertificateAuthorityProvider, LocalCertificateAuthorityProvider>();

        services.AddSingleton<SharedCertificateService>();

        // Default to in-memory CA store (for tests); CosmosDb registration replaces this.
        services.TryAddSingleton<ICertificateAuthorityStore, ConfigurationCertificateAuthorityStore>();
        services.TryAddSingleton<IClientCertificateStore, InMemoryClientCertificateStore>();

        return services;
    }
}
