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

    public AuthConfigurationHttp(IOptions<UserAuthenticationOptions> options)
    {
        _configuration = Describe(options.Value);
    }

    [Function(nameof(GetAuthConfiguration))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAuthConfiguration, Definitions.Tags.Access, Description = "Anonymous. Tells clients which identity provider signs users in and with which public settings.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AuthConfiguration), Description = "The user identity provider of this deployment.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public IActionResult GetAuthConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Auth.V1.Configuration)]
        HttpRequestData request)
    {
        return new OkObjectResult(_configuration);
    }

    // Only public identifiers. The WorkOS management key and other secrets stay out.
    internal static AuthConfiguration Describe(UserAuthenticationOptions options)
    {
        if (options.Provider == IdentityProviderKind.AuthKit)
        {
            var authKitDomain = options.AuthKit.AuthKitDomain.TrimEnd('/');

            return new AuthConfiguration
            {
                Provider = nameof(IdentityProviderKind.AuthKit),
                ClientId = options.AuthKit.ClientId,
                AuthKitDomain = string.IsNullOrWhiteSpace(authKitDomain) ? null : authKitDomain,
            };
        }

        var clientId = options.ClientId ?? string.Empty;

        // The same scopes sform requests today.
        return new AuthConfiguration
        {
            Provider = nameof(IdentityProviderKind.EntraId),
            ClientId = clientId,
            TenantId = options.TenantId,
            Scopes =
            [
                $"api://{clientId}/{Scopes.User.Impersonation}",
                $"api://{clientId}/{Scopes.Default.Write}",
            ],
        };
    }
}
