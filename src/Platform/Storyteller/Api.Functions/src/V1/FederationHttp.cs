using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

// Workload identity federation: the trusted issuers of a project (read by Contributors, written by Administrators),
// the federation rules of its machines (Contributors, like the machines) and the anonymous RFC 8693 token endpoint,
// where a Federated machine exchanges a platform token for a short-lived Storyteller token.
public class FederationHttp
{
    // A platform token (at most MachineAuth:Federation:MaxSubjectTokenBytes) and a few parameters.
    public const int MaxTokenRequestBytes = 32 * 1024;

    // The 409 for a re-created issuer names at most this many machines whose rules still reference it.
    public const int MaxMachinesNamedInConflict = 10;

    // The Functions host serves every route below /api; host.json sets no other routePrefix.
    public const string RoutePrefix = "/api";

    private const string RetryAfterSeconds = "5";

    private static readonly JsonSerializerOptions TokenJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // RFC 8693 section 2.1 lets audience and resource repeat; any other parameter appears once (RFC 6749 section 3.2).
    private static readonly HashSet<string> RepeatableParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        MachineTokenRequest.AudienceName,
        MachineTokenRequest.ResourceName,
    };

    private static readonly HashSet<string> KnownParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        MachineTokenRequest.GrantTypeName,
        MachineTokenRequest.SubjectTokenName,
        MachineTokenRequest.SubjectTokenTypeName,
        MachineTokenRequest.ClientIdName,
        MachineTokenRequest.AudienceName,
        MachineTokenRequest.ResourceName,
        MachineTokenRequest.ScopeName,
        MachineTokenRequest.RequestedTokenTypeName,
        MachineTokenRequest.ActorTokenName,
        MachineTokenRequest.ActorTokenTypeName,
    };

    private static readonly string[] SubjectTokenTypes =
    [
        OAuthTokenExchange.TokenTypes.Jwt,
        OAuthTokenExchange.TokenTypes.IdToken,
        OAuthTokenExchange.TokenTypes.AccessToken,
    ];

    private static readonly string[] RequestedTokenTypes =
    [
        OAuthTokenExchange.TokenTypes.AccessToken,
        OAuthTokenExchange.TokenTypes.Jwt,
    ];

    private readonly IAccessService _accessService;
    private readonly IFederatedIssuerStore _issuerStore;
    private readonly IFederatedTokenExchange _tokenExchange;
    private readonly FederationOptions _options;
    private readonly JsonSerializerOptions _bodyJsonOptions;
    private readonly ILogger<FederationHttp> _logger;

    public FederationHttp(
        IAccessService accessService,
        IFederatedIssuerStore issuerStore,
        IFederatedTokenExchange tokenExchange,
        IOptions<FederationOptions> options,
        IOptions<JsonOptions> jsonOptions,
        ILogger<FederationHttp> logger)
    {
        _accessService = accessService;
        _issuerStore = issuerStore;
        _tokenExchange = tokenExchange;
        _options = options.Value;
        _bodyJsonOptions = jsonOptions.Value.JsonSerializerOptions;
        _logger = logger;
    }

    [Function(nameof(GetFederatedIssuers))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetFederatedIssuers, Definitions.Tags.Access, Description = "The trusted issuers of the project: the OIDC issuers whose tokens its Federated machines exchange for Storyteller tokens. Contributor.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<FederatedIssuer>), Description = "The trusted issuers of the project, ordered by name.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetFederatedIssuers(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.FederatedIssuers)]
        HttpRequestData request,
        string organization,
        string project)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        var issuers = await _issuerStore.ListAsync(organization, project, request.FunctionContext.CancellationToken);
        return new OkObjectResult(issuers);
    }

    [Function(nameof(GetFederatedIssuer))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetFederatedIssuer, Definitions.Tags.Access, Description = "A trusted issuer of the project. Contributor.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Name, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.FederatedIssuerName)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(FederatedIssuer), Description = "The trusted issuer.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The project has no trusted issuer of that name.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetFederatedIssuer(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.FederatedIssuer)]
        HttpRequestData request,
        string organization,
        string project,
        string name)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        var issuer = await _issuerStore.GetAsync(organization, project, name, request.FunctionContext.CancellationToken);

        return issuer is null
            ? new NotFoundResult()
            : new OkObjectResult(issuer);
    }

    [Function(nameof(PutFederatedIssuer))]
    [OpenApiOperation(Definitions.RouteIds.Access.SetFederatedIssuer, Definitions.Tags.Access, Description = "Creates or replaces a trusted issuer of the project. The route name wins over the body; the Issuer of an existing trusted issuer cannot change. Administrator.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Name, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.FederatedIssuerName)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(FederatedIssuer), Description = "The trusted issuer: the exact `iss`, where its keys come from, the longest accepted token lifetime and the claims every token must carry.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(FederatedIssuer), Description = "The stored trusted issuer.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The issuer is not valid, its Issuer would change, or the project already has 20 trusted issuers (ErrorCode InvalidFederation).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "Federation rules of machines still reference the name of a deleted issuer (ErrorCode FederatedIssuerInUse); the message names up to 10 machines.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PutFederatedIssuer(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.Access.V1.FederatedIssuer)]
        HttpRequestData request,
        string organization,
        string project,
        string name)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Administrator);
        var cancellationToken = request.FunctionContext.CancellationToken;
        var (issuer, invalidBody) = await request.ReadJsonBodyAsync<FederatedIssuer>(_bodyJsonOptions, "a trusted issuer", ErrorCodes.InvalidFederation);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        // An empty body (or null) is an issuer without an Issuer, which the validation names.
        var requested = (issuer ?? new FederatedIssuer()) with { Name = name };

        if (FederationValidation.ValidateIssuer(requested, _options) is { } invalid)
        {
            return InvalidFederation(invalid);
        }

        var existing = await _issuerStore.GetAsync(organization, project, name, cancellationToken);

        if (existing is null)
        {
            // Rules left behind by a deleted issuer would silently trust whatever issuer gets the name next.
            var machines = await _accessService.FindMachinesUsingIssuerAsync(organization, project, name, MaxMachinesNamedInConflict);

            if (machines.Count > 0)
            {
                var more = machines.Count >= MaxMachinesNamedInConflict ? " and maybe more" : string.Empty;
                return ErrorResponseMapping.ToActionResult(new ConflictException(
                    $"Federation rules still reference the trusted issuer '{name}' (machines {string.Join(", ", machines)}{more}); change those rules or delete the machines before creating it again.",
                    ErrorCodes.FederatedIssuerInUse));
            }
        }
        else if (!string.Equals(existing.Issuer, requested.Issuer, StringComparison.Ordinal))
        {
            return InvalidFederation(FederationValidation.IssuerImmutableMessage);
        }

        try
        {
            var stored = await _issuerStore.SetAsync(organization, project, requested, cancellationToken);
            return new OkObjectResult(stored);
        }
        catch (InvalidInputException exception)
        {
            // The store keeps the limit of issuers and the immutable Issuer under concurrent writes.
            return ErrorResponseMapping.ToActionResult(exception);
        }
    }

    [Function(nameof(DeleteFederatedIssuer))]
    [OpenApiOperation(Definitions.RouteIds.Access.DeleteFederatedIssuer, Definitions.Tags.Access, Description = "Deletes a trusted issuer even while federation rules reference it: the kill switch. Those rules stop matching within a minute; Storyteller tokens already issued stay valid until they expire. Administrator.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Name, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.FederatedIssuerName)]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The project has no trusted issuer of that name.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteFederatedIssuer(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Access.V1.FederatedIssuer)]
        HttpRequestData request,
        string organization,
        string project,
        string name)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Administrator);

        var deleted = await _issuerStore.DeleteAsync(organization, project, name, request.FunctionContext.CancellationToken);

        return deleted
            ? new OkResult()
            : new NotFoundResult();
    }

    [Function(nameof(PutMachineFederationRules))]
    [OpenApiOperation(Definitions.RouteIds.Access.SetMachineFederationRules, Definitions.Tags.Access, Description = "Replaces the federation rules of a Federated machine; they apply to new token exchanges. Contributor.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdMachine)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(IEnumerable<FederationRule>), Description = "1 to 10 rules; a platform token must match one of them.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineAccess), Description = "The machine access with its new rules and its token endpoint.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "A rule is not valid (ErrorCode InvalidFederation), or the machine is not Federated (ErrorCode MachineNotFederated).")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The machine access doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The machine was changed concurrently while its rules were replaced (ErrorCode Conflict); reload it and retry.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PutMachineFederationRules(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.Access.V1.MachineFederationRules)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);
        var cancellationToken = request.FunctionContext.CancellationToken;

        if (!MachineIds.IsValid(id))
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = Definitions.Errors.InvalidMachineId });
        }

        var (rules, invalidBody) = await request.ReadJsonBodyAsync<List<FederationRule>>(_bodyJsonOptions, "a list of federation rules", ErrorCodes.InvalidFederation);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        // An empty body (or null) is no rule, which the validation names.
        IReadOnlyList<FederationRule> requested = rules ?? [];
        var issuers = await _issuerStore.ListAsync(organization, project, cancellationToken);

        if (FederationValidation.ValidateRules(requested, issuers) is { } invalid)
        {
            return InvalidFederation(invalid);
        }

        try
        {
            var machine = await _accessService.SetMachineFederationRulesAsync(organization, project, id, requested);
            return new OkObjectResult(WithTokenEndpoint(machine, request, _options, organization, project));
        }
        catch (Exception exception) when (exception is NotFoundException or InvalidInputException or ConflictException)
        {
            return ErrorResponseMapping.ToActionResult(exception);
        }
    }

    [Function(nameof(PostMachineToken))]
    [OpenApiOperation(Definitions.RouteIds.Access.ExchangeMachineToken, Definitions.Tags.Access, Description = "Anonymous. The RFC 8693 token exchange of Federated machines: client_id names the machine, subject_token is a token of a trusted issuer of the project, and the response is a short-lived Storyteller token for Authorization: Bearer. Send no Authorization header. Every refused subject token gets the same invalid_request description; the reason is only logged.")]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiRequestBody(Definitions.ContentTypes.FormUrlEncoded, typeof(MachineTokenRequest), Required = true, Description = "The token request form. audience and resource may repeat; every other parameter appears once, and an empty value counts as absent.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineTokenResponse), Description = "The Storyteller token (Cache-Control: no-store).")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(OAuthErrorResponse), Description = "invalid_request (also for every refused subject token), unsupported_grant_type, invalid_target or invalid_scope.")]
    [OpenApiResponseWithBody(HttpStatusCode.ServiceUnavailable, Definitions.ContentTypes.Json, typeof(OAuthErrorResponse), Description = "temporarily_unavailable: the keys of the trusted issuer or the signing key are not available right now; Retry-After says when to try again.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<HttpResponseData> PostMachineToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.MachineToken)]
        HttpRequestData request,
        string organization,
        string project)
    {
        var cancellationToken = request.FunctionContext.CancellationToken;
        var (tokenRequest, error) = await ParseTokenRequestAsync(request, organization, project, _options, cancellationToken);

        if (error is not null)
        {
            _logger.LogInformation(
                "A token request for {Organization}.{Project} is refused: {Error} {Description}",
                organization,
                project,
                error.Error,
                error.ErrorDescription);
            return await RespondAsync(request, HttpStatusCode.BadRequest, error);
        }

        var result = await _tokenExchange.ExchangeAsync(tokenRequest!, cancellationToken);

        if (result.Succeeded)
        {
            return await RespondAsync(request, HttpStatusCode.OK, new MachineTokenResponse
            {
                AccessToken = result.AccessToken!,
                IssuedTokenType = result.IssuedTokenType!,
                TokenType = OAuthTokenExchange.BearerTokenType,
                ExpiresIn = result.ExpiresIn,
                Scope = result.Scope!,
            });
        }

        var body = new OAuthErrorResponse { Error = result.Error!, ErrorDescription = result.ErrorDescription };
        return await RespondAsync(request, result.IsUnavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadRequest, body);
    }

    // {PublicBaseUrl, or the request's scheme and host with /api}/v1/{organization}/{project}/access/token. Never from
    // X-Forwarded-Host: workloads send their platform tokens wherever this URL points.
    public static string GetTokenEndpoint(HttpRequestData request, FederationOptions options, string organization, string project)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            ? $"{request.Url.Scheme}://{request.Url.Authority}{RoutePrefix}"
            : options.PublicBaseUrl.TrimEnd('/');
        var route = Definitions.Routes.Access.V1.MachineToken
            .Replace($"{{{Definitions.Parameters.Organization}}}", Uri.EscapeDataString(organization), StringComparison.Ordinal)
            .Replace($"{{{Definitions.Parameters.Project}}}", Uri.EscapeDataString(project), StringComparison.Ordinal);

        return $"{baseUrl}/{route}";
    }

    // Federated machines get the project's token endpoint on create, get, list and the rules PUT; it is not stored.
    public static MachineAccess WithTokenEndpoint(MachineAccess machine, HttpRequestData request, FederationOptions options, string organization, string project)
    {
        return machine.CredentialKind == MachineCredentialKind.Federated
            ? machine with { TokenEndpoint = GetTokenEndpoint(request, options, organization, project) }
            : machine;
    }

    // Step 1 of the exchange: the transport, the form and the parameters, before any read. Either the request for
    // IFederatedTokenExchange or the OAuth error to answer with 400.
    internal static async Task<(FederatedTokenExchangeRequest? Request, OAuthErrorResponse? Error)> ParseTokenRequestAsync(
        HttpRequestData request,
        string organization,
        string project,
        FederationOptions options,
        CancellationToken cancellationToken)
    {
        // A bearer header would already have been validated as an API credential; the subject token is the credential.
        if (request.Headers.Contains("Authorization"))
        {
            return Refuse("The token request must not carry an Authorization header.");
        }

        if (!IsFormContent(request.Headers))
        {
            return Refuse($"The content type must be {Definitions.ContentTypes.FormUrlEncoded}.");
        }

        var body = await ReadBodyAsync(request.Body, cancellationToken);

        if (body is null)
        {
            return Refuse($"The request body exceeds {MaxTokenRequestBytes} bytes.");
        }

        // An empty value counts as absent (RFC 6749 section 3.2).
        var parameters = QueryHelpers.ParseQuery(body)
            .Select(parameter => (Name: parameter.Key, Values: parameter.Value.Where(value => !string.IsNullOrEmpty(value)).Select(value => value!).ToArray()))
            .Where(parameter => parameter.Values.Length > 0)
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Values, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, values) in parameters)
        {
            if (values.Length > 1 && !RepeatableParameters.Contains(name))
            {
                return Refuse(KnownParameters.Contains(name)
                    ? $"The parameter {name.ToLowerInvariant()} is given more than once."
                    : "A parameter is given more than once.");
            }
        }

        var grantType = GetSingle(parameters, MachineTokenRequest.GrantTypeName);

        if (grantType is null)
        {
            return Missing(MachineTokenRequest.GrantTypeName);
        }

        if (!string.Equals(grantType, OAuthTokenExchange.GrantType, StringComparison.Ordinal))
        {
            return Refuse("Only the token exchange grant type is supported.", OAuthTokenExchange.Errors.UnsupportedGrantType);
        }

        var subjectToken = GetSingle(parameters, MachineTokenRequest.SubjectTokenName);

        if (subjectToken is null)
        {
            return Missing(MachineTokenRequest.SubjectTokenName);
        }

        var subjectTokenType = GetSingle(parameters, MachineTokenRequest.SubjectTokenTypeName);

        if (subjectTokenType is null)
        {
            return Missing(MachineTokenRequest.SubjectTokenTypeName);
        }

        if (!SubjectTokenTypes.Contains(subjectTokenType, StringComparer.Ordinal))
        {
            return Refuse("The subject token type is not supported.");
        }

        var clientId = GetSingle(parameters, MachineTokenRequest.ClientIdName);

        if (clientId is null)
        {
            return Missing(MachineTokenRequest.ClientIdName);
        }

        if (parameters.ContainsKey(MachineTokenRequest.ActorTokenName) || parameters.ContainsKey(MachineTokenRequest.ActorTokenTypeName))
        {
            return Refuse("Actor tokens are not supported.");
        }

        var targets = GetAll(parameters, MachineTokenRequest.AudienceName).Concat(GetAll(parameters, MachineTokenRequest.ResourceName));

        if (targets.Any(target => !string.Equals(target, options.Audience, StringComparison.Ordinal)))
        {
            return Refuse("The audience or resource is not this API.", OAuthTokenExchange.Errors.InvalidTarget);
        }

        var requestedTokenType = GetSingle(parameters, MachineTokenRequest.RequestedTokenTypeName) ?? OAuthTokenExchange.TokenTypes.AccessToken;

        if (!RequestedTokenTypes.Contains(requestedTokenType, StringComparer.Ordinal))
        {
            return Refuse("The requested token type is not supported.");
        }

        // Only the syntax here; the subset of the machine's scopes is checked once the machine is known.
        var scope = GetSingle(parameters, MachineTokenRequest.ScopeName);

        if (scope is not null && !IsScopeSyntax(scope))
        {
            return Refuse("The parameter scope is malformed.", OAuthTokenExchange.Errors.InvalidScope);
        }

        return (new FederatedTokenExchangeRequest
        {
            Organization = organization,
            Project = project,
            MachineId = clientId,
            SubjectToken = subjectToken,
            SubjectTokenType = subjectTokenType,
            Scopes = scope?.Split(' '),
            RequestedTokenType = requestedTokenType,
        }, null);
    }

    private static (FederatedTokenExchangeRequest? Request, OAuthErrorResponse? Error) Refuse(
        string description,
        string error = OAuthTokenExchange.Errors.InvalidRequest)
    {
        return (null, new OAuthErrorResponse { Error = error, ErrorDescription = description });
    }

    private static (FederatedTokenExchangeRequest? Request, OAuthErrorResponse? Error) Missing(string name)
    {
        return Refuse($"The parameter {name} is missing.");
    }

    private static string? GetSingle(Dictionary<string, string[]> parameters, string name)
    {
        return parameters.TryGetValue(name, out var values) ? values[0] : null;
    }

    private static string[] GetAll(Dictionary<string, string[]> parameters, string name)
    {
        return parameters.TryGetValue(name, out var values) ? values : [];
    }

    // RFC 6749 section 3.3: scope-tokens of %x21 / %x23-5B / %x5D-7E, separated by single spaces.
    private static bool IsScopeSyntax(string scope)
    {
        return scope.Split(' ').All(token => token.Length > 0
            && token.All(character => character is '\x21' or (>= '\x23' and <= '\x5B') or (>= '\x5D' and <= '\x7E')));
    }

    // One Content-Type of application/x-www-form-urlencoded; parameters such as charset are allowed.
    private static bool IsFormContent(HttpHeadersCollection headers)
    {
        if (!headers.TryGetValues("Content-Type", out var values))
        {
            return false;
        }

        var contentTypes = values.ToList();

        return contentTypes.Count == 1
            && MediaTypeHeaderValue.TryParse(contentTypes[0], out var mediaType)
            && string.Equals(mediaType.MediaType, Definitions.ContentTypes.FormUrlEncoded, StringComparison.OrdinalIgnoreCase);
    }

    // Null when the body is longer than MaxTokenRequestBytes; nothing beyond one more byte is read.
    private static async Task<string?> ReadBodyAsync(Stream body, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxTokenRequestBytes + 1];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await body.ReadAsync(buffer.AsMemory(length), cancellationToken);

            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return length > MaxTokenRequestBytes ? null : Encoding.UTF8.GetString(buffer, 0, length);
    }

    // OAuth responses go out as they are, not through ErrorResponse, and are never cached (RFC 6749 section 5.1).
    private static async Task<HttpResponseData> RespondAsync(HttpRequestData request, HttpStatusCode statusCode, object body)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        response.Headers.Add("Pragma", "no-cache");

        if (statusCode == HttpStatusCode.ServiceUnavailable)
        {
            response.Headers.Add("Retry-After", RetryAfterSeconds);
        }

        await response.WriteBytesAsync(JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), TokenJsonOptions));
        return response;
    }

    private static BadRequestObjectResult InvalidFederation(string message)
    {
        return new BadRequestObjectResult(new ErrorResponse(message) { ErrorCode = ErrorCodes.InvalidFederation });
    }
}
