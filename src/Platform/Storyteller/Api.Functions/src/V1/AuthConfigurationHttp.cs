using System.Net;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.V1;

public class AuthConfigurationHttp
{
    private readonly AuthConfiguration _configuration;

    public AuthConfigurationHttp(IOptions<UserAuthenticationOptions> options, IOptions<TokenExchangeOptions>? tokenExchangeOptions = null)
    {
        _configuration = Describe(options.Value, tokenExchangeOptions?.Value);
    }

    [Function(nameof(GetAuthConfiguration))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAuthConfiguration, Definitions.Tags.Access, Description = "Anonymous. Tells clients which identity provider signs users in and with which public settings, and whether the user token exchange is enabled.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AuthConfiguration), Description = "The user identity provider of this deployment.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public IActionResult GetAuthConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Auth.V1.Configuration)]
        HttpRequestData request)
    {
        return new OkObjectResult(_configuration);
    }

    // Only public identifiers. The WorkOS management key and other secrets stay out, and so do the actors of the exchange.
    internal static AuthConfiguration Describe(UserAuthenticationOptions options, TokenExchangeOptions? tokenExchangeOptions = null)
    {
        var tokenExchange = new TokenExchangeConfiguration { Enabled = tokenExchangeOptions?.Enabled == true };

        if (options.Provider == IdentityProviderKind.AuthKit)
        {
            var authKitDomain = options.AuthKit.AuthKitDomain.TrimEnd('/');

            return new AuthConfiguration
            {
                Provider = nameof(IdentityProviderKind.AuthKit),
                ClientId = options.AuthKit.ClientId,
                AuthKitDomain = string.IsNullOrWhiteSpace(authKitDomain) ? null : authKitDomain,
                TokenExchange = tokenExchange,
            };
        }

        if (options.Provider == IdentityProviderKind.Keycloak)
        {
            // Clients find the endpoints in the realm's OpenID metadata under the issuer.
            return new AuthConfiguration
            {
                Provider = nameof(IdentityProviderKind.Keycloak),
                ClientId = options.Keycloak.ClientId?.Trim() ?? string.Empty,
                Issuer = options.Keycloak.GetIssuer(),
                TokenExchange = tokenExchange,
            };
        }

        var clientId = options.ClientId ?? string.Empty;

        // A user needs only User.Impersonation; the role decides the rest. sform requests the same.
        return new AuthConfiguration
        {
            Provider = nameof(IdentityProviderKind.EntraId),
            ClientId = clientId,
            TenantId = options.TenantId,
            Scopes =
            [
                $"api://{clientId}/{Scopes.User.Impersonation}",
            ],
            TokenExchange = tokenExchange,
        };
    }
}
