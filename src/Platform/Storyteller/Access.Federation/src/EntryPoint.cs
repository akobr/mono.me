using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public static class EntryPoint
{
    // Federated machines: the token exchange, Storyteller tokens and their validator, next to any ClientCredentials
    // provider (federation issues no identity provider credentials). Call it after AddCertificateMachineAccess, whose
    // router sends the Federated kind here. Cosmos DB stores registered before win over the in-memory ones.
    public static IServiceCollection AddFederatedMachineAccess(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(FederatedMachineAccessService)))
        {
            return services;
        }

        services.AddFederationOptions(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddHttpClient(FederationMetadataHttpHandler.HttpClientName, client =>
            {
                client.Timeout = FederationMetadataHttpHandler.Timeout;
                client.MaxResponseContentBufferSize = FederationMetadataHttpHandler.MaxResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
                FederationMetadataHttpHandler.Create(provider.GetRequiredService<IOptions<FederationOptions>>().Value));

        services.TryAddSingleton<IFederatedIssuerStore, InMemoryFederatedIssuerStore>();
        services.TryAddSingleton<ITokenSigningKeyStore, InMemoryTokenSigningKeyStore>();

        services.AddSingleton<FederatedIssuerKeyResolver>();
        services.AddSingleton<SubjectTokenVerifier>();
        services.AddSingleton<TokenSigningKeyProvider>();
        services.AddSingleton<StorytellerTokenIssuer>();
        services.AddSingleton<FederatedTokenExchangeService>();
        services.AddSingleton<IFederatedTokenExchange>(provider => provider.GetRequiredService<FederatedTokenExchangeService>());
        services.AddSingleton<FederatedMachineAccessService>();
        services.AddSingleton<IFederatedMachineAccessService>(provider => provider.GetRequiredService<FederatedMachineAccessService>());

        // Next to the validator of a ClientCredentials provider, not instead of it.
        services.AddSingleton<IMachineTokenValidator, StorytellerTokenValidator>();
        return services;
    }
}
