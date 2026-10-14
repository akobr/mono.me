using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace _42.Platform.Storyteller.Api.V1;

// The key/value pairs of a view. The API decides who the caller is (GetSecretRightsAsync) and the service
// enforces the secret rights, because only the service knows whether a key is a secret. Domain errors
// (403 AccessDenied and MtlsRequired, 404, 409, 412, 424, 501, 503) come from the middleware.
public class KeyValuesHttp
{
    private readonly IKeyValueService _keyValues;
    private readonly IAccessService _access;
    private readonly IKeyValuePolicyStore _policies;
    private readonly ILogger<KeyValuesHttp> _logger;

    public KeyValuesHttp(
        IKeyValueService keyValues,
        IAccessService access,
        IKeyValuePolicyStore policies,
        ILogger<KeyValuesHttp> logger)
    {
        _keyValues = keyValues;
        _access = access;
        _policies = policies;
        _logger = logger;
    }

    [Function(nameof(GetKeyValues))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetKeyValues, Definitions.Tags.KeyValues, Description = "The pairs of the view, ordered by key, up to 1000 per page. Values of public pairs only with includeValues; secret values are never listed.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.KeyPrefix, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only keys starting with this prefix (case-insensitive).")]
    [OpenApiParameter(Definitions.Parameters.Protection, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only these protection levels (Public, Standard, Hsm, Byok, Hyok); repeatable or comma-separated.")]
    [OpenApiParameter(Definitions.Parameters.State, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Active (default), Deleted or All.")]
    [OpenApiParameter(Definitions.Parameters.IncludeValues, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "Include the values of public pairs.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValuesResponse), Description = "One page of pairs.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetKeyValues(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.KeyValues)]
        HttpRequestData request,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(KeyValueAuthorization.ReadScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var listRequest = new KeyValueListRequest
        {
            KeyPrefix = request.Query[Definitions.Parameters.KeyPrefix],
            Protections = KeyValueQuery.GetProtections(request),
            State = KeyValueQuery.GetEnum<KeyValueState>(request, Definitions.Parameters.State) ?? KeyValueState.Active,
            IncludeValues = KeyValueQuery.GetBool(request, Definitions.Parameters.IncludeValues),
            ContinuationToken = request.Query[Definitions.Parameters.ContinuationToken],
        };

        var response = await _keyValues.ListAsync(organization, project, view, listRequest);
        return new OkObjectResult(response);
    }

    [Function(nameof(GetKeyValue))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetKeyValue, Definitions.Tags.KeyValues, Description = "The current version of a pair. A secret's value only with reveal=true and secret rights. " + Definitions.Descriptions.NoStore)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.KeyValueKey)]
    [OpenApiParameter(Definitions.Parameters.Reveal, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "Return the value of a secret (ContributorWithSecrets, or Configuration.Secrets under the project's mTLS rule).")]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "Also find a soft-deleted key.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValue), Description = "The pair.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseMtlsRequired)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key does not exist (ErrorCode KeyValueNotFound).")]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseFailedDependency)]
    [OpenApiResponseWithBody(HttpStatusCode.ServiceUnavailable, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseServiceUnavailable)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetKeyValue(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.KeyValue)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(KeyValueAuthorization.ReadScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var options = new KeyValueReadOptions
        {
            Reveal = KeyValueQuery.GetBool(request, Definitions.Parameters.Reveal),
            IncludeDeleted = KeyValueQuery.GetBool(request, Definitions.Parameters.IncludeDeleted),
        };

        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();

        if (options.Reveal && rights.IsWithheldByMtls)
        {
            throw KeyValueErrors.MtlsRequired();
        }

        var pair = await _keyValues.GetAsync(organization, project, view, key, options, rights.Caller);

        if (pair is null)
        {
            return NotFound(view, key);
        }

        return WithSecretHeaders(request, pair);
    }

    [Function(nameof(SetKeyValue))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.SetKeyValue, Definitions.Tags.KeyValues, Description = "Creates or updates a pair as a new version. Protection is required on create and kept on update when missing. A secret level, or a change to or from a secret level, needs secret rights. The response never carries a secret value.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.KeyValueKey)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(KeyValueWrite), Required = true, Description = "The value, the content type, the protection level (required on create) and an optional expected version (0 = the key must not exist).")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValue), Description = "The new version, without a secret value.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "InvalidKey, ValueRequired, ValueTooLarge, InvalidContentType, ProtectionRequired, InvalidSecretReference, InvalidClientEncryptedValue or UnknownRecipient.")]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseMtlsRequired)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The level is not allowed in the project (ProtectionNotAllowed), or the key changed concurrently too often (KeyValueBusy).")]
    [OpenApiResponseWithBody(HttpStatusCode.PreconditionFailed, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The expected version does not match (KeyValueVersionMismatch).")]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseFailedDependency)]
    [OpenApiResponseWithBody(HttpStatusCode.NotImplemented, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotImplemented)]
    [OpenApiResponseWithBody(HttpStatusCode.ServiceUnavailable, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseServiceUnavailable)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetKeyValue(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.KeyValues.V1.KeyValue)]
        HttpRequestData request,
        [FromBody] KeyValueWrite? write,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(KeyValueAuthorization.WriteScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (write is null || write.Value is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("A body with Value is required."));
        }

        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();
        var pair = await KeyValueAuthorization.UnderMtlsRuleAsync(
            rights,
            () => _keyValues.SetAsync(organization, project, view, key, write, rights.Caller));

        // the write confirms the version, never the secret itself
        return new OkObjectResult(pair.IsSecret ? pair with { Value = null } : pair);
    }

    [Function(nameof(DeleteKeyValue))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.DeleteKeyValue, Definitions.Tags.KeyValues, Description = "Soft-deletes a pair (restorable for the deleted retention, 90 days by default), or removes it with every version when permanent=true (Administrator).")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.KeyValueKey)]
    [OpenApiParameter(Definitions.Parameters.Permanent, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "Remove the key and every version for good; Administrator only.")]
    [OpenApiParameter(Definitions.Parameters.ExpectedVersion, In = ParameterLocation.Query, Required = false, Type = typeof(long), Description = "Delete only when the key is at this version.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Deleted.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseMtlsRequired)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key does not exist, or is already deleted (ErrorCode KeyValueNotFound).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key changed concurrently too often (KeyValueBusy).")]
    [OpenApiResponseWithBody(HttpStatusCode.PreconditionFailed, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The expected version does not match (KeyValueVersionMismatch).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteKeyValue(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.KeyValues.V1.KeyValue)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        var options = new KeyValueDeleteOptions
        {
            Permanent = KeyValueQuery.GetBool(request, Definitions.Parameters.Permanent),
            ExpectedVersion = KeyValueQuery.GetVersion(request, Definitions.Parameters.ExpectedVersion),
        };

        if (options.Permanent)
        {
            await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);
        }
        else
        {
            request.CheckScope(KeyValueAuthorization.WriteScopes);
            await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);
        }

        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();
        var deleted = await KeyValueAuthorization.UnderMtlsRuleAsync(
            rights,
            () => _keyValues.DeleteAsync(organization, project, view, key, options, rights.Caller));

        return deleted ? new NoContentResult() : NotFound(view, key);
    }

    [Function(nameof(RestoreKeyValues))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.RestoreKeyValues, Definitions.Tags.KeyValues, Description = "Restores soft-deleted keys (a list of at most 100, or a prefix of at most 1000) to their latest version, without a new version. Secrets need secret rights; each key gets its own outcome.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(KeyValueBulkRequest), Required = true, Description = "Keys, or a key prefix.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValueBulkResult), Description = "The outcome per key: Done, Unchanged, NotFound, Forbidden or Failed.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> RestoreKeyValues(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.KeyValues.V1.Restore)]
        HttpRequestData request,
        [FromBody] KeyValueBulkRequest? bulk,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(KeyValueAuthorization.WriteScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!TryValidateBulk(bulk, out var badRequest))
        {
            return badRequest;
        }

        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();
        var result = await _keyValues.RestoreAsync(organization, project, view, bulk, rights.Caller);
        return new OkObjectResult(result);
    }

    [Function(nameof(PurgeKeyValues))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.PurgeKeyValues, Definitions.Tags.KeyValues, Description = "Removes soft-deleted keys with every version, before their retention ends. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(KeyValueBulkRequest), Required = true, Description = "Keys, or a key prefix.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValueBulkResult), Description = "The outcome per key: Done, NotFound or Failed.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PurgeKeyValues(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.KeyValues.V1.Purge)]
        HttpRequestData request,
        [FromBody] KeyValueBulkRequest? bulk,
        string organization,
        string project,
        string view)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);

        if (!TryValidateBulk(bulk, out var badRequest))
        {
            return badRequest;
        }

        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();
        var result = await _keyValues.PurgeAsync(organization, project, view, bulk, rights.Caller);
        return new OkObjectResult(result);
    }

    [Function(nameof(ProtectKeyValues))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.ProtectKeyValues, Definitions.Tags.KeyValues, Description = "Moves keys between the server-side levels Standard, Hsm and Byok as a new version each; older versions keep their level until they expire. Public and Hyok need a new value from the client (NotAllowed). Needs secret rights.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(KeyValueBulkRequest), Required = true, Description = "Keys or a key prefix, and the target Protection.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValueBulkResult), Description = "The outcome per key: Done (with the new version), Unchanged, NotFound, Forbidden, NotAllowed or Failed.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseMtlsRequired)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The target level is not allowed in the project (ProtectionNotAllowed).")]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseFailedDependency)]
    [OpenApiResponseWithBody(HttpStatusCode.NotImplemented, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotImplemented)]
    [OpenApiResponseWithBody(HttpStatusCode.ServiceUnavailable, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseServiceUnavailable)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> ProtectKeyValues(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.KeyValues.V1.Protect)]
        HttpRequestData request,
        [FromBody] KeyValueBulkRequest? bulk,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(KeyValueAuthorization.WriteScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!TryValidateBulk(bulk, out var badRequest))
        {
            return badRequest;
        }

        if (bulk.Protection is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("The target Protection is required.") { ErrorCode = ErrorCodes.ProtectionRequired });
        }

        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();
        KeyValueAuthorization.EnsureSecretRights(rights);
        var result = await _keyValues.ProtectAsync(organization, project, view, bulk, rights.Caller);
        return new OkObjectResult(result);
    }

    [Function(nameof(GetKeyValueVersions))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetKeyValueVersions, Definitions.Tags.KeyValues, Description = "Every stored version of a key, oldest first, with its level, author, creation time and expiry.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.KeyValueKey)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<KeyValueVersion>), Description = "The versions, oldest first.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key has no stored version (ErrorCode KeyValueNotFound).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetKeyValueVersions(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.Versions)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(KeyValueAuthorization.ReadScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var versions = await _keyValues.GetVersionsAsync(organization, project, view, key);
        return versions.Count == 0 ? NotFound(view, key) : new OkObjectResult(versions);
    }

    [Function(nameof(GetKeyValueVersion))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetKeyValueVersion, Definitions.Tags.KeyValues, Description = "One version of a key. A secret's value only with reveal=true and secret rights. " + Definitions.Descriptions.NoStore)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.KeyValueKey)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = Definitions.Descriptions.KeyValueVersion)]
    [OpenApiParameter(Definitions.Parameters.Reveal, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "Return the value of a secret version.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValue), Description = "The version.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseMtlsRequired)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The version does not exist (ErrorCode KeyValueNotFound).")]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseFailedDependency)]
    [OpenApiResponseWithBody(HttpStatusCode.ServiceUnavailable, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseServiceUnavailable)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetKeyValueVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.Version)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version)
    {
        request.CheckScope(KeyValueAuthorization.ReadScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var reveal = KeyValueQuery.GetBool(request, Definitions.Parameters.Reveal);
        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();

        if (reveal && rights.IsWithheldByMtls)
        {
            throw KeyValueErrors.MtlsRequired();
        }

        var pair = await _keyValues.GetVersionAsync(organization, project, view, key, version, reveal, rights.Caller);

        if (pair is null)
        {
            return new NotFoundObjectResult(new ErrorResponse($"The version {version} of the key '{key}' does not exist in view '{view}'.") { ErrorCode = ErrorCodes.KeyValueNotFound });
        }

        return WithSecretHeaders(request, pair);
    }

    [Function(nameof(GetKeyValueVersionDiff))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetKeyValueVersionDiff, Definitions.Tags.KeyValues, Description = "The text diff between a version and the one before it (version 0 is the empty text). Secret versions need secret rights; client-encrypted values cannot be compared (400 NotSupportedForProtection).")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.KeyValueKey)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = Definitions.Descriptions.KeyValueVersion)]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between this and the previous version. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseMtlsRequired)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The version does not exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseFailedDependency)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetKeyValueVersionDiff(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.VersionDiff)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version)
    {
        return DiffAsync(request, organization, project, view, key, version > 0 ? version - 1u : 0u, version);
    }

    [Function(nameof(GetKeyValueVersionDiffCustom))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetKeyValueVersionDiffCustom, Definitions.Tags.KeyValues, Description = "The text diff between two versions of a key.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.KeyValueKey)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The target version number (to).")]
    [OpenApiParameter(Definitions.Parameters.VersionFrom, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The source version number (from).")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between the two versions. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseMtlsRequired)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "One of the versions does not exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseFailedDependency)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetKeyValueVersionDiffCustom(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.VersionDiffCustom)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version,
        uint versionFrom)
    {
        return DiffAsync(request, organization, project, view, key, versionFrom, version);
    }

    [Function(nameof(ExportKeyValues))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.ExportKeyValues, Definitions.Tags.KeyValues, Description = "The pairs of the view as a 2s.key-values.v1 document. Secret values only with IncludeSecrets and secret rights; otherwise secrets are listed without a value. A POST, so that nothing secret travels in a URL. " + Definitions.Descriptions.NoStore)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(KeyValueExportRequest), Required = false, Description = "An optional key prefix, protection levels and IncludeSecrets.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValueDocument), Description = "The document.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseMtlsRequired)]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseFailedDependency)]
    [OpenApiResponseWithBody(HttpStatusCode.ServiceUnavailable, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseServiceUnavailable)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> ExportKeyValues(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.KeyValues.V1.Export)]
        HttpRequestData request,
        [FromBody] KeyValueExportRequest? export,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(KeyValueAuthorization.ReadScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        export ??= new KeyValueExportRequest();
        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();

        if (export.IncludeSecrets)
        {
            KeyValueAuthorization.EnsureSecretRights(rights);
            request.MarkNoStore();
        }

        var document = await _keyValues.ExportAsync(organization, project, view, export, rights.Caller);
        return new OkObjectResult(document);
    }

    [Function(nameof(ImportKeyValues))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.ImportKeyValues, Definitions.Tags.KeyValues, Description = "Writes a 2s.key-values.v1 document: Merge (default) writes the listed keys, Replace also soft-deletes the other active keys (under keyPrefix, when given). Items with a secret need secret rights; a secret item without a value leaves the stored secret unchanged. dryRun reports the actions without writing.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Mode, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Merge (default) or Replace.")]
    [OpenApiParameter(Definitions.Parameters.DryRun, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "Report the actions without writing.")]
    [OpenApiParameter(Definitions.Parameters.KeyPrefix, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Replace only the keys under this prefix.")]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(KeyValueDocument), Required = true, Description = "The document.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValueImportResult), Description = "The action per item: Create, Update, Unchanged or Delete, with the version after a write.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The document is invalid (ImportInvalid, with the failing items in Details); nothing was written.")]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseMtlsRequired)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "A level is not allowed in the project (ProtectionNotAllowed).")]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseFailedDependency)]
    [OpenApiResponseWithBody(HttpStatusCode.NotImplemented, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotImplemented)]
    [OpenApiResponseWithBody(HttpStatusCode.ServiceUnavailable, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseServiceUnavailable)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> ImportKeyValues(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.KeyValues.V1.Import)]
        HttpRequestData request,
        [FromBody] KeyValueDocument? document,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(KeyValueAuthorization.WriteScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (document?.Items is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("A document with Items is required.") { ErrorCode = ErrorCodes.ImportInvalid });
        }

        var options = new KeyValueImportOptions
        {
            Mode = KeyValueQuery.GetEnum<KeyValueImportMode>(request, Definitions.Parameters.Mode) ?? KeyValueImportMode.Merge,
            DryRun = KeyValueQuery.GetBool(request, Definitions.Parameters.DryRun),
            KeyPrefix = request.Query[Definitions.Parameters.KeyPrefix],
        };

        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();
        var result = await KeyValueAuthorization.UnderMtlsRuleAsync(
            rights,
            () => _keyValues.ImportAsync(organization, project, view, document, options, rights.Caller));

        _logger.LogInformation("Key/value import into {Organization}/{Project}/{View}: {Count} items, dry run {DryRun}", organization, project, view, result.Items.Count, result.IsDryRun);
        return new OkObjectResult(result);
    }

    private static bool TryValidateBulk([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] KeyValueBulkRequest? bulk, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out IActionResult? badRequest)
    {
        if (bulk is null || ((bulk.Keys is null || bulk.Keys.Count == 0) && bulk.KeyPrefix is null))
        {
            badRequest = new BadRequestObjectResult(new ErrorResponse("A body with Keys or a KeyPrefix is required."));
            return false;
        }

        badRequest = null;
        return true;
    }

    private static IActionResult NotFound(string view, string key)
    {
        return new NotFoundObjectResult(new ErrorResponse($"The key '{key}' does not exist in view '{view}'.") { ErrorCode = ErrorCodes.KeyValueNotFound });
    }

    private static IActionResult WithSecretHeaders(HttpRequestData request, KeyValue pair)
    {
        if (pair.IsSecret && pair.Value is not null)
        {
            request.MarkNoStore();
        }

        return new OkObjectResult(pair);
    }

    private async Task<IActionResult> DiffAsync(HttpRequestData request, string organization, string project, string view, string key, ulong fromVersion, ulong toVersion)
    {
        request.CheckScope(KeyValueAuthorization.ReadScopes);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();

        if (rights.CanReadSecrets)
        {
            // the diff of a secret carries its text; a public diff is harmless to withhold from caches too
            request.MarkNoStore();
        }

        return await KeyValueAuthorization.UnderMtlsRuleAsync(
            rights,
            () => _keyValues.GetVersionChangesAsync(organization, project, view, key, fromVersion, toVersion, rights.Caller).ToDiffResponseAsync(request));
    }
}
