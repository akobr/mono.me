using System.Security.Cryptography.X509Certificates;

using _42.Platform.Storyteller.Mcp.Hosting;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.AppConfig;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.TokenCacheProviders.InMemory;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// Entra ID on-behalf-of: the person's MCP token is the assertion, the MCP app's credential the proof, and the result a
// delegated token for the Storyteller API whose azp is the MCP app (listed in the API's Auth:DelegatedClients).
public sealed class EntraOnBehalfOfTokenProvider : IStorytellerTokenProvider
{
    public const long CacheSizeLimit = 100L * 1024 * 1024;

    public const string TokenExchangeAudience = "api://AzureADTokenExchange";

    // Inbound tokens live about an hour; a cached entry has no use after its assertion is gone.
    public static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(2);

    private readonly Lazy<IConfidentialClientApplication> _application;
    private readonly IReadOnlyList<string> _scopes;

    public EntraOnBehalfOfTokenProvider(IOptions<McpHostOptions> options, IOptions<StorytellerApiOptions> api, McpAuthSettings settings)
    {
        var entra = options.Value.EntraId;
        _scopes = api.Value.Scopes.ToArray();
        _application = new Lazy<IConfidentialClientApplication>(() => CreateApplication(entra, settings));
    }

    public async Task<string> GetTokenAsync(string userToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userToken);

        try
        {
            // The partitioned cache answers first; Entra is asked only for a new assertion or an expired token.
            var result = await _application.Value
                .AcquireTokenOnBehalfOf(_scopes, new UserAssertion(userToken))
                .ExecuteAsync(cancellationToken);

            return result.AccessToken;
        }
        catch (MsalUiRequiredException exception)
        {
            var message = string.IsNullOrEmpty(exception.Claims)
                ? StorytellerSignInRequiredException.ConsentMessage
                : StorytellerSignInRequiredException.ClaimsChallengeMessage;

            throw new StorytellerSignInRequiredException(message, exception.ErrorCode);
        }
        catch (MsalException exception)
        {
            throw new StorytellerSignInFailedException(exception.ErrorCode);
        }
    }

    private static IConfidentialClientApplication CreateApplication(McpEntraIdOptions entra, McpAuthSettings settings)
    {
        var builder = ConfidentialClientApplicationBuilder
            .Create(settings.ClientId)
            .WithAuthority($"{settings.Authority}/");

        if (!string.IsNullOrWhiteSpace(entra.ClientSecret))
        {
            builder = builder.WithClientSecret(entra.ClientSecret);
        }
        else if (!string.IsNullOrWhiteSpace(entra.CertificatePath))
        {
            builder = builder.WithCertificate(X509CertificateLoader.LoadPkcs12FromFile(entra.CertificatePath, entra.CertificatePassword));
        }
        else
        {
            // A federated credential of the MCP app trusts the managed identity: no secret is stored anywhere.
            var identity = string.IsNullOrWhiteSpace(entra.ManagedIdentityClientId)
                ? ManagedIdentityId.SystemAssigned
                : ManagedIdentityId.WithUserAssignedClientId(entra.ManagedIdentityClientId);
            var managedIdentity = ManagedIdentityApplicationBuilder.Create(identity).Build();

            builder = builder.WithClientAssertion(async (AssertionRequestOptions request) =>
            {
                var assertion = await managedIdentity
                    .AcquireTokenForManagedIdentity(TokenExchangeAudience)
                    .ExecuteAsync(request.CancellationToken);

                return assertion.AccessToken;
            });
        }

        var application = builder.Build();
        application.AddInMemoryTokenCache(services =>
        {
            services.Configure<MsalMemoryTokenCacheOptions>(cache => cache.AbsoluteExpirationRelativeToNow = CacheExpiration);
            services.Configure<MemoryCacheOptions>(cache => cache.SizeLimit = CacheSizeLimit);
        });

        return application;
    }
}
