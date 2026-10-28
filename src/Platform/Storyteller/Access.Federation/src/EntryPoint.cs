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

    // The user token exchange (Auth:TokenExchange) behind POST v1/access/token: actors, their proofs and Storyteller user
    // tokens. Call it after AddFederatedMachineAccess, whose issuer, signing keys and key resolver it shares, and next to
    // the user registration of the deployment's provider, which registers IUserSubjectTokenValidator. Idempotent.
    public static IServiceCollection AddUserTokenExchange(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(UserTokenExchangeService)))
        {
            return services;
        }

        // Registering the federation here could put its in-memory stores before the Cosmos DB ones.
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(FederatedMachineAccessService)))
        {
            throw new InvalidOperationException($"Call {nameof(AddFederatedMachineAccess)} before {nameof(AddUserTokenExchange)}.");
        }

        services.AddUserAuthenticationOptions(configuration);
        services.AddSingleton<ActorAuthenticator>();
        services.AddSingleton<UserTokenExchangeService>();
        services.AddSingleton<IUserTokenExchange>(provider => provider.GetRequiredService<UserTokenExchangeService>());
        services.AddHostedService<TokenExchangeActorNotice>();
        return services;
    }
}
