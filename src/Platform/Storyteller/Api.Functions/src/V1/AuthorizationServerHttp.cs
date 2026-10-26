using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.V1.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.V1;

// Discovery of the Storyteller issuer under the API base: RFC 8414 metadata, which names the user token exchange, and
// the JWKS of the keys Storyteller tokens are signed with. Both are anonymous. Shared caches may always keep the JWKS,
// and the metadata only when MachineAuth:Federation:PublicBaseUrl names its URLs.
public class AuthorizationServerHttp
{
    public const string PrivateKeyJwt = "private_key_jwt";
    public const string ClientSecretPost = "client_secret_post";

    // Five minutes: a new signing key reaches verifiers soon after it is stored.
    public const string PublicCacheControl = "public, max-age=300";

    private readonly TokenSigningKeyProvider _keyProvider;
    private readonly FederationOptions _options;
    private readonly bool _isDevelopment;
    private readonly ILogger<AuthorizationServerHttp> _logger;

    public AuthorizationServerHttp(
        TokenSigningKeyProvider keyProvider,
        IOptions<FederationOptions> options,
        IHostEnvironment environment,
        ILogger<AuthorizationServerHttp> logger)
    {
        _keyProvider = keyProvider;
        _options = options.Value;
        _isDevelopment = environment.IsDevelopment();
        _logger = logger;
    }

    [Function(nameof(GetAuthorizationServerMetadata))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAuthorizationServerMetadata, Definitions.Tags.Access, Description = "Anonymous. RFC 8414 metadata of the Storyteller issuer (MachineAuth:Federation:Issuer): the token endpoint of the user token exchange, how actors authenticate there, and the JWKS of the signing keys. URLs start with MachineAuth:Federation:PublicBaseUrl, else with the request's scheme and host.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AuthorizationServerMetadata), Description = "The metadata (Cache-Control: public, max-age=300 with MachineAuth:Federation:PublicBaseUrl; no-store without it, because its URLs then come from the request's host).")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<HttpResponseData> GetAuthorizationServerMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.WellKnown.AuthorizationServer)]
        HttpRequestData request)
    {
        var baseUrl = OAuthTokenForm.GetApiBaseUrl(request, _options);

        // client_secret_post exists for local development only; the exchange refuses it elsewhere.
        string[] authMethods = _isDevelopment ? [PrivateKeyJwt, ClientSecretPost] : [PrivateKeyJwt];

        var response = OAuthTokenForm.CreateJsonResponse(request, HttpStatusCode.OK);

        // Without PublicBaseUrl the URLs come from the Host header; a shared cache that does not key on it must not keep them.
        response.Headers.Add("Cache-Control", string.IsNullOrWhiteSpace(_options.PublicBaseUrl) ? "no-store" : PublicCacheControl);
        await OAuthTokenForm.WriteJsonAsync(response, new AuthorizationServerMetadata
        {
            Issuer = _options.Issuer,
            TokenEndpoint = TokenExchangeHttp.GetTokenEndpoint(request, _options),
            JwksUri = $"{baseUrl}/{Definitions.Routes.Access.WellKnown.Jwks}",
            ResponseTypesSupported = [],
            GrantTypesSupported = [OAuthTokenExchange.GrantType],
            TokenEndpointAuthMethodsSupported = authMethods,
            TokenEndpointAuthSigningAlgValuesSupported = [.. SubjectTokenVerifier.AllowedAlgorithms],
        });
        return response;
    }

    [Function(nameof(GetJsonWebKeySet))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetJsonWebKeySet, Definitions.Tags.Access, Description = "Anonymous. The RFC 7517 JWK Set of the public keys Storyteller tokens are signed with: the keys the API accepts, today the one stored key. Empty while no key is stored.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(PublicJsonWebKeySet), Description = "The public keys (Cache-Control: public, max-age=300; no-cache while there is none).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.ServiceUnavailable, Description = "The signing key could not be read right now; Retry-After says when to try again.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<HttpResponseData> GetJsonWebKeySet(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.WellKnown.Jwks)]
        HttpRequestData request)
    {
        IReadOnlyList<StorytellerJsonWebKey> keys;

        try
        {
            // Never creates a key, and reads the store no more often than the validation of tokens does.
            keys = await _keyProvider.GetPublicJsonWebKeysAsync(request.FunctionContext.CancellationToken);
        }
        catch (BearerKeyRetrievalException exception)
        {
            _logger.LogWarning(exception, "The JWKS of Storyteller tokens is unavailable: the signing key could not be read");
            var unavailable = request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            unavailable.Headers.Add("Cache-Control", "no-store");
            unavailable.Headers.Add("Retry-After", OAuthTokenForm.RetryAfterSeconds);
            return unavailable;
        }

        var response = OAuthTokenForm.CreateJsonResponse(request, HttpStatusCode.OK);

        // No key yet: the first issued token creates one, which verifiers must not miss for minutes.
        response.Headers.Add("Cache-Control", keys.Count > 0 ? PublicCacheControl : "no-cache");
        await OAuthTokenForm.WriteJsonAsync(response, new PublicJsonWebKeySet
        {
            Keys = [.. keys.Select(key => new PublicJsonWebKey { Kty = key.Kty, Kid = key.Kid, Use = key.Use, Alg = key.Alg, N = key.N, E = key.E })],
        });
        return response;
    }
}
