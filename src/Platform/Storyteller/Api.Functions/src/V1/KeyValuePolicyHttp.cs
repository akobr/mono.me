using System.Net;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.KeyValues.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

// The key/value policy of a project: allowed levels, the mTLS rule, the HSM and customer keys, rotation and the
// recipients of client-encrypted values. Readers see it, Administrators change it; machines only read.
public class KeyValuePolicyHttp
{
    private readonly IKeyValuePolicyService _policies;
    private readonly IKeyValuePolicyStore _policyStore;
    private readonly IAccessService _access;
    private readonly JsonSerializerOptions _bodyJsonOptions;

    public KeyValuePolicyHttp(IKeyValuePolicyService policies, IKeyValuePolicyStore policyStore, IAccessService access, IOptions<JsonOptions> jsonOptions)
    {
        _policies = policies;
        _policyStore = policyStore;
        _access = access;
        _bodyJsonOptions = jsonOptions.Value.JsonSerializerOptions;
    }

    [Function(nameof(GetKeyValuePolicy))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetKeyValuePolicy, Definitions.Tags.KeyValues, Description = "The key/value policy of the project: the allowed levels, the default secret level, the mTLS rule, the keys in use, the customer key, the recipients and a re-wrap in progress.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValuePolicy), Description = "The policy; the defaults when none was stored yet.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The project does not exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetKeyValuePolicy(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.Policy)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.ConfigurationRead);

        var policy = await _policies.GetAsync(organization, project, request.FunctionContext.CancellationToken);
        return new OkObjectResult(policy);
    }

    [Function(nameof(SetKeyValuePolicy))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.SetKeyValuePolicy, Definitions.Tags.KeyValues, Description = "Changes the policy; a missing member keeps its value. Enabling Hsm creates the project's HSM key, Byok needs a verified customer key, Hyok an enabled recipient. Disabling a level that keys still carry fails with 409 ProtectionInUse. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(KeyValuePolicyUpdate), Required = true, Description = "The members to change, and an optional ExpectedVersion.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValuePolicy), Description = "The policy after the change.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The project does not exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "ProtectionInUse (the counts per view in Details), ByokNotConfigured, or PolicyVersionMismatch.")]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The customer key failed the verification (CustomerKeyInvalid).")]
    [OpenApiResponseWithBody(HttpStatusCode.NotImplemented, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotImplemented)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetKeyValuePolicy(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.KeyValues.V1.Policy)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);
        var (update, invalidBody) = await request.ReadJsonBodyAsync<KeyValuePolicyUpdate>(_bodyJsonOptions, "a key/value policy update", RequestBodyLimits.Control);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (update is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("A body with the policy members to change is required."));
        }

        var caller = await GetCallerAsync(request, organization, project);
        var policy = await _policies.UpdateAsync(organization, project, update, caller, request.FunctionContext.CancellationToken);
        return new OkObjectResult(policy);
    }

    [Function(nameof(GetByokSetup))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetByokSetup, Definitions.Tags.KeyValues, Description = "What a customer needs to bring their own key: the platform's application id, the admin-consent URL for the customer's tenant, and the role to grant on the key. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ByokSetupInfo), Description = "The setup information.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotImplemented, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "Customer-managed keys are not configured in this deployment (ProtectionNotConfigured).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetByokSetup(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.ByokSetup)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);
        return new OkObjectResult(_policies.GetByokSetup(organization, project));
    }

    [Function(nameof(VerifyByok))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.VerifyByok, Definitions.Tags.KeyValues, Description = "Checks the customer key set in the policy: enabled, RSA of at least 2048 bits, wrapKey and unwrapKey allowed, and a wrap/unwrap round trip. Records VerifiedAt and the current key version. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValuePolicy), Description = "The policy with the verification recorded.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "No customer key is set (ByokNotConfigured).")]
    [OpenApiResponseWithBody(HttpStatusCode.FailedDependency, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key failed a check (CustomerKeyInvalid) or is not reachable (CustomerKeyUnavailable).")]
    [OpenApiResponseWithBody(HttpStatusCode.NotImplemented, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotImplemented)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> VerifyByok(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.KeyValues.V1.ByokVerify)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);

        var caller = await GetCallerAsync(request, organization, project);
        var policy = await _policies.VerifyByokAsync(organization, project, caller, request.FunctionContext.CancellationToken);
        return new OkObjectResult(policy);
    }

    [Function(nameof(RotateKeyValueKey))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.RotateKeyValueKey, Definitions.Tags.KeyValues, Description = "Rotates the project's Standard or Hsm key-encryption key now and starts the re-wrap of the stored envelopes; the progress is in the policy's Rewrap. The customer rotates a Byok key in their own vault. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(KeyValueRotateRequest), Required = true, Description = "The level whose key rotates: Standard or Hsm.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(KeyValuePolicy), Description = "The policy with the new key version and the re-wrap status.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The level has no key the platform rotates.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotImplemented, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotImplemented)]
    [OpenApiResponseWithBody(HttpStatusCode.ServiceUnavailable, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseServiceUnavailable)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> RotateKeyValueKey(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.KeyValues.V1.Rotate)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);
        var (rotate, invalidBody) = await request.ReadJsonBodyAsync<KeyValueRotateRequest>(_bodyJsonOptions, "a key rotation request", RequestBodyLimits.Control);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (rotate is null || !Enum.IsDefined(rotate.Protection))
        {
            return new BadRequestObjectResult(new ErrorResponse("A body with the Protection to rotate is required."));
        }

        var caller = await GetCallerAsync(request, organization, project);
        var policy = await _policies.RotateAsync(organization, project, rotate.Protection, caller, request.FunctionContext.CancellationToken);
        return new OkObjectResult(policy);
    }

    [Function(nameof(GetRecipients))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.GetRecipients, Definitions.Tags.KeyValues, Description = "The recipients of client-encrypted (Hyok) values: ids and RSA public keys the clients encrypt for.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<ClientEncryptionRecipient>), Description = "The recipients.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetRecipients(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.KeyValues.V1.Recipients)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.ConfigurationRead);

        var policy = await _policies.GetAsync(organization, project, request.FunctionContext.CancellationToken);
        return new OkObjectResult(policy.Recipients);
    }

    [Function(nameof(AddRecipient))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.AddRecipient, Definitions.Tags.KeyValues, Description = "Registers a recipient: a name and an RSA public key of at least 3072 bits as PEM or JWK; the id (the JWE kid) is generated when missing. At most 20 per project. Existing values are not re-encrypted for it. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(ClientEncryptionRecipientCreate), Required = true, Description = "The optional id, the name and the public key.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ClientEncryptionRecipient), Description = "The recipient with its public key as a JWK.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key is not RSA, too short, or the id is malformed (RecipientInvalid).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The id exists (RecipientExists), or the project has 20 recipients.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> AddRecipient(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.KeyValues.V1.Recipients)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);
        var (create, invalidBody) = await request.ReadJsonBodyAsync<ClientEncryptionRecipientCreate>(_bodyJsonOptions, "a client encryption recipient", RequestBodyLimits.Recipient, ErrorCodes.RecipientInvalid);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (create is null || string.IsNullOrWhiteSpace(create.Name) || string.IsNullOrWhiteSpace(create.PublicKey))
        {
            return new BadRequestObjectResult(new ErrorResponse("A body with Name and PublicKey is required.") { ErrorCode = ErrorCodes.RecipientInvalid });
        }

        var caller = await GetCallerAsync(request, organization, project);
        var recipient = await _policies.AddRecipientAsync(organization, project, create, caller, request.FunctionContext.CancellationToken);
        return new OkObjectResult(recipient);
    }

    [Function(nameof(UpdateRecipient))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.UpdateRecipient, Definitions.Tags.KeyValues, Description = "Renames, enables or disables a recipient. The last enabled recipient cannot be disabled while Hyok is allowed. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.RecipientId)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(ClientEncryptionRecipientUpdate), Required = true, Description = "The new name and/or IsEnabled.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ClientEncryptionRecipient), Description = "The recipient after the change.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The recipient does not exist (RecipientNotFound).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The last enabled recipient of a project that allows Hyok (ProtectionInUse).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> UpdateRecipient(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Patch, Route = Definitions.Routes.KeyValues.V1.Recipient)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);
        var (update, invalidBody) = await request.ReadJsonBodyAsync<ClientEncryptionRecipientUpdate>(_bodyJsonOptions, "a client encryption recipient update", RequestBodyLimits.Control);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (update is null || (update.Name is null && update.IsEnabled is null))
        {
            return new BadRequestObjectResult(new ErrorResponse("A body with Name or IsEnabled is required."));
        }

        var caller = await GetCallerAsync(request, organization, project);
        var recipient = await _policies.UpdateRecipientAsync(organization, project, id, update, caller, request.FunctionContext.CancellationToken);
        return new OkObjectResult(recipient);
    }

    [Function(nameof(RemoveRecipient))]
    [OpenApiOperation(Definitions.RouteIds.KeyValues.RemoveRecipient, Definitions.Tags.KeyValues, Description = "Removes a recipient. Values already encrypted for it stay readable by the holder of its private key until they are encrypted again. Administrator only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.RecipientId)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "Removed.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The recipient does not exist (RecipientNotFound).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The last enabled recipient of a project that allows Hyok (ProtectionInUse).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> RemoveRecipient(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.KeyValues.V1.Recipient)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        await KeyValueAuthorization.CheckAdministratorAsync(request, _access, organization, project);

        var caller = await GetCallerAsync(request, organization, project);
        await _policies.RemoveRecipientAsync(organization, project, id, caller, request.FunctionContext.CancellationToken);
        return new NoContentResult();
    }

    private async Task<KeyValues.KeyValueCaller> GetCallerAsync(HttpRequestData request, string organization, string project)
    {
        var rights = (await request.GetSecretRightsAsync(_access, _policyStore, organization, project)).AsAmbient();
        return rights.Caller;
    }
}
