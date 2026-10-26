using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.V1.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.V1;

// The anonymous RFC 8693 token endpoint of the user token exchange (Auth:TokenExchange): an actor such as the MCP server
// sends a person's token with its own proof and gets a short-lived Storyteller user token that names both. This class
// checks the transport and the form; IUserTokenExchange checks every value, the actor, the person and the scope.
public class TokenExchangeHttp
{
    // Two JWTs (the subject token, and an actor token or a client assertion, each at most
    // MachineAuth:Federation:MaxSubjectTokenBytes) and a few parameters.
    public const int MaxTokenRequestBytes = 64 * 1024;

    private static readonly HashSet<string> KnownParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        UserTokenRequest.GrantTypeName,
        UserTokenRequest.SubjectTokenName,
        UserTokenRequest.SubjectTokenTypeName,
        UserTokenRequest.ActorTokenName,
        UserTokenRequest.ActorTokenTypeName,
        UserTokenRequest.ClientAssertionName,
        UserTokenRequest.ClientAssertionTypeName,
        UserTokenRequest.ClientIdName,
        UserTokenRequest.ClientSecretName,
        UserTokenRequest.AudienceName,
        UserTokenRequest.ResourceName,
        UserTokenRequest.ScopeName,
        UserTokenRequest.RequestedTokenTypeName,
    };

    private readonly IUserTokenExchange _tokenExchange;
    private readonly FederationOptions _options;
    private readonly ILogger<TokenExchangeHttp> _logger;

    public TokenExchangeHttp(
        IUserTokenExchange tokenExchange,
        IOptions<FederationOptions> options,
        ILogger<TokenExchangeHttp> logger)
    {
        _tokenExchange = tokenExchange;
        _options = options.Value;
        _logger = logger;
    }

    [Function(nameof(PostUserToken))]
    [OpenApiOperation(Definitions.RouteIds.Access.ExchangeUserToken, Definitions.Tags.Access, Description = "Anonymous. The RFC 8693 exchange of user tokens by actors (Auth:TokenExchange:Actors), such as the MCP server: subject_token is the person's token for the actor, client_id names the actor, and actor_token, client_assertion or (Development only) client_secret proves it. The response is a short-lived Storyteller user token for Authorization: Bearer that names the person and the actor (act), with the actor's restrictions. The exchange never creates accounts. Send no Authorization header. Each refusal has one fixed error_description; the reason is only logged.")]
    [OpenApiRequestBody(Definitions.ContentTypes.FormUrlEncoded, typeof(UserTokenRequest), Required = true, Description = "The token request form, at most 64 KiB. audience and resource may repeat; every other parameter appears once, and an empty value counts as absent.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(UserTokenResponse), Description = "The Storyteller user token and the actor's restrictions (Cache-Control: no-store).")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(OAuthErrorResponse), Description = "invalid_request, invalid_client (an unknown actor or a bad proof), invalid_grant (a refused subject token, the replay limit, or no Storyteller account for the person), unsupported_grant_type (also while the exchange is off), invalid_target or invalid_scope.")]
    [OpenApiResponseWithBody(HttpStatusCode.ServiceUnavailable, Definitions.ContentTypes.Json, typeof(OAuthErrorResponse), Description = "temporarily_unavailable: the keys to verify the request or the signing key are not available right now; Retry-After says when to try again.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<HttpResponseData> PostUserToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.UserToken)]
        HttpRequestData request)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;
        var (parameters, formError) = await OAuthTokenForm.ReadAsync(request, MaxTokenRequestBytes, KnownParameters, cancellationToken);

        if (parameters is null)
        {
            _logger.LogInformation("A user token request is refused: {Description}", formError);
            return await OAuthTokenForm.RespondAsync(request, HttpStatusCode.BadRequest, new OAuthErrorResponse
            {
                Error = OAuthTokenExchange.Errors.InvalidRequest,
                ErrorDescription = formError,
            });
        }

        var result = await _tokenExchange.ExchangeAsync(ToExchangeRequest(parameters, _options), cancellationToken);

        if (result.Succeeded)
        {
            return await OAuthTokenForm.RespondAsync(request, HttpStatusCode.OK, new UserTokenResponse
            {
                AccessToken = result.AccessToken!,
                IssuedTokenType = result.IssuedTokenType!,
                TokenType = OAuthTokenExchange.BearerTokenType,
                ExpiresIn = result.ExpiresIn,
                Scope = result.Scope!,
                Restrictions = [.. result.Restrictions],
            });
        }

        var body = new OAuthErrorResponse { Error = result.Error!, ErrorDescription = result.ErrorDescription };
        return await OAuthTokenForm.RespondAsync(request, result.IsUnavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadRequest, body);
    }

    // {PublicBaseUrl, or the request's scheme and host with /api}/v1/access/token, as the metadata publishes it.
    public static string GetTokenEndpoint(HttpRequestData request, FederationOptions options)
    {
        return $"{OAuthTokenForm.GetApiBaseUrl(request, options)}/{Definitions.Routes.Access.V1.UserToken}";
    }

    // The token endpoint a client assertion may name as its aud, next to the Storyteller issuer: only one built from
    // PublicBaseUrl. A URL from the request's Host header would accept an assertion the actor made for another server.
    // Null without PublicBaseUrl, which the start check (TokenExchangeOptionsValidator) allows only when no actor uses
    // ClientAssertion; ActorAuthenticator then still accepts only the issuer.
    internal static string? GetConfiguredTokenEndpoint(FederationOptions options)
    {
        return string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            ? null
            : $"{options.PublicBaseUrl.TrimEnd('/')}/{Definitions.Routes.Access.V1.UserToken}";
    }

    // The parameters as they are; the exchange checks their values.
    private static UserTokenExchangeRequest ToExchangeRequest(Dictionary<string, string[]> parameters, FederationOptions options)
    {
        return new UserTokenExchangeRequest
        {
            GrantType = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.GrantTypeName),
            ClientId = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.ClientIdName),
            SubjectToken = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.SubjectTokenName),
            SubjectTokenType = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.SubjectTokenTypeName),
            ActorToken = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.ActorTokenName),
            ActorTokenType = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.ActorTokenTypeName),
            ClientAssertion = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.ClientAssertionName),
            ClientAssertionType = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.ClientAssertionTypeName),
            ClientSecret = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.ClientSecretName),
            Audiences = OAuthTokenForm.GetAll(parameters, UserTokenRequest.AudienceName),
            Resources = OAuthTokenForm.GetAll(parameters, UserTokenRequest.ResourceName),
            Scope = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.ScopeName),
            RequestedTokenType = OAuthTokenForm.GetSingle(parameters, UserTokenRequest.RequestedTokenTypeName),
            TokenEndpoint = GetConfiguredTokenEndpoint(options),
        };
    }
}
