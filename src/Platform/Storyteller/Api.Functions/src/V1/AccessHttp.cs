using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

public class AccessHttp
{
    private readonly IAccessService _accessService;
    private readonly ICertificateAuthorityProvider? _caProvider;
    private readonly IMachineAuthenticationPolicyStore? _policyStore;
    private readonly IUserProfileResolver? _profileResolver;
    private readonly MachineAuthenticationOptions _machineOptions;
    private readonly FederationOptions _federationOptions;
    private readonly UserAuthenticationOptions _authOptions;
    private readonly JsonSerializerOptions _bodyJsonOptions;
    private readonly ILogger<AccessHttp> _logger;

    public AccessHttp(
        IAccessService accessService,
        IOptions<JsonOptions> jsonOptions,
        ILogger<AccessHttp> logger,
        ICertificateAuthorityProvider? caProvider = null,
        IMachineAuthenticationPolicyStore? policyStore = null,
        IUserProfileResolver? profileResolver = null,
        IOptions<MachineAuthenticationOptions>? machineOptions = null,
        IOptions<FederationOptions>? federationOptions = null,
        IOptions<UserAuthenticationOptions>? authOptions = null)
    {
        _accessService = accessService;
        _bodyJsonOptions = jsonOptions.Value.JsonSerializerOptions;
        _logger = logger;
        _caProvider = caProvider;
        _policyStore = policyStore;
        _profileResolver = profileResolver;
        _machineOptions = machineOptions?.Value ?? new MachineAuthenticationOptions();
        _federationOptions = federationOptions?.Value ?? new FederationOptions();
        _authOptions = authOptions?.Value ?? new UserAuthenticationOptions();
    }

    [Function(nameof(GetAccount))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAccount, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Account), Description = Definitions.Descriptions.ResponseAccount)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The account doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAccount(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.Account)]
        HttpRequestData request)
    {
        request.EnsureUser();
        var accountId = request.GetIdentityUniqueId();
        _logger.LogWarning("GET api/access/account call with accountId: {accountId}", accountId);
        var account = await _accessService.GetAccountAsync(accountId);

        return account is null
            ? new NotFoundResult()
            : new OkObjectResult(account);
    }

    [Function(nameof(PostAccount))]
    [OpenApiOperation(Definitions.RouteIds.Access.CreateAccount, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(Models.AccountCreate), Description = "Organization and project for the new account, and the description of the project.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Account), Description = "Newly created account.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "The account already exists, only one of the organization and the project is given, or a project description is given without them.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostAccount(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.Account)]
        HttpRequestData request)
    {
        request.EnsureUser();
        var accountId = request.GetRegistrationAccountId(_authOptions.Provider);
        _logger.LogWarning("POST api/access/account call with accountId: {accountId}", accountId);
        var account = await _accessService.GetAccountAsync(accountId);

        if (account is not null)
        {
            return new BadRequestResult();
        }

        var (accountModel, invalidBody) = await request.ReadJsonBodyAsync<Models.AccountCreate>(_bodyJsonOptions, "an account", RequestBodyLimits.Control);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        // An account without memberships is created with {}; no body at all is a mistake of the client.
        if (accountModel is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("The account is missing."));
        }

        if (string.IsNullOrWhiteSpace(accountModel.Organization) != string.IsNullOrWhiteSpace(accountModel.Project))
        {
            return new BadRequestObjectResult(new ErrorResponse("The organization and the project are given together, or not at all."));
        }

        // The description goes to the project the account creates; without one, it has nowhere to go.
        if (!string.IsNullOrWhiteSpace(accountModel.ProjectDescription) && string.IsNullOrWhiteSpace(accountModel.Project))
        {
            return new BadRequestObjectResult(new ErrorResponse("A project description needs an organization and a project."));
        }

        var (userName, name) = await request.GetIdentityProfileAsync(_profileResolver);
        var internalAccountModel = new AccountCreate
        {
            IdentityId = accountId,
            UserName = userName,
            Name = name,
            Organization = accountModel.Organization,
            Project = accountModel.Project,
            ProjectDescription = accountModel.ProjectDescription,
        };
        account = await _accessService.CreateAccountAsync(internalAccountModel);

        return new OkObjectResult(account);
    }

    [Function(nameof(GetAccessPoints))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAccessPoints, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<AccessPoint>), Description = "The list of all manageable organizations and projects. The access points where the account is Admin or Owner.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAccessPoints(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.AccessPoints)]
        HttpRequestData request)
    {
        request.EnsureUser();
        var accountId = request.GetIdentityUniqueId();
        _logger.LogWarning("GET api/access/points call with accountId: {accountId}", accountId);
        var points = await _accessService.GetAccessPointsAsync(accountId);
        return new OkObjectResult(points);
    }

    [Function(nameof(GetAccessPoint))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAccessPoint, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The key of requested access point.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessPoint), Description = "The access point (organization or project).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAccessPoint(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.AccessPoint)]
        HttpRequestData request,
        string key)
    {
        var pointKey = key.Trim().ToLowerInvariant();
        // TODO: [P2] check if pointKey is valid and expected format
        await request.CheckUserAccessToAsync(_accessService, pointKey, AccountRole.Administrator);
        var point = await _accessService.GetAccessPointAsync(pointKey);
        return new OkObjectResult(point);
    }

    [Function(nameof(PostAccessPoints))]
    [OpenApiOperation(Definitions.RouteIds.Access.CreateAccessPoint, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(AccessPointCreate), Description = "An organization or project to create, and the description of the project.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessPoint), Description = "The created access point (organization or project).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostAccessPoints(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.AccessPoints)]
        HttpRequestData request)
    {
        request.EnsureUser();
        var accountId = request.GetIdentityUniqueId();
        _logger.LogInformation("POST api/access/points call with accountId: {accountId}", accountId);
        var (pointModel, invalidBody) = await request.ReadJsonBodyAsync<AccessPointCreate>(_bodyJsonOptions, "an access point", RequestBodyLimits.Control);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (pointModel is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("The access point is missing."));
        }

        // The caller owns what they create; clients do not send the owner.
        pointModel = pointModel with { OwnerId = accountId };
        var point = await _accessService.CreateAccessPointAsync(pointModel);
        return new OkObjectResult(point);
    }

    [Function(nameof(PutAccessPoint))]
    [OpenApiOperation(Definitions.RouteIds.Access.UpdateAccessPoint, Definitions.Tags.Access, Description = "Sets the description of a project, trimmed; a blank one clears it. Needs a user with the Administrator role.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The key of the project (organization.project).")]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(AccessPointUpdate), Description = "The new description; empty or no body clears it.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessPoint), Description = "The updated project.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key is not organization.project, or the body is not a project update.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The project doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The project changed concurrently twice in a row (Conflict); try again.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PutAccessPoint(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.Access.V1.AccessPoint)]
        HttpRequestData request,
        string key)
    {
        request.EnsureUser("Describing a project needs a user with the Administrator role.");
        var pointKey = key.Trim().ToLowerInvariant();
        await request.CheckUserAccessToAsync(_accessService, pointKey, AccountRole.Administrator);

        // Only a project has a description.
        if (!TrySplitProjectKey(pointKey, out _, out _, out var badRequestResult))
        {
            return badRequestResult;
        }

        var (model, invalidBody) = await request.ReadJsonBodyAsync<AccessPointUpdate>(_bodyJsonOptions, "a project update", RequestBodyLimits.Control);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        // Without a body, the description is cleared, as for views.
        var point = await _accessService.UpdateAccessPointAsync(pointKey, model ?? new AccessPointUpdate());
        return new OkObjectResult(point);
    }

    [Function(nameof(GetProjects))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetProjects, Definitions.Tags.Access, Description = "Every project where the caller has a role, with the role and the description, ordered by key. The members and the policies of a project are not included.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<ProjectSummary>), Description = "The caller's projects.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The account doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetProjects(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.Projects)]
        HttpRequestData request)
    {
        request.EnsureUser("Listing projects needs a user.");
        var accountId = request.GetIdentityUniqueId();
        var projects = await _accessService.GetProjectsAsync(accountId);
        return new OkObjectResult(projects);
    }

    [Function(nameof(PostGrantPermission))]
    [OpenApiOperation(Definitions.RouteIds.Access.GrantUserAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(Permission), Description = "The requested permission to grant.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessPoint), Description = "The affected access point (organization or project).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostGrantPermission(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.Grant)]
        HttpRequestData request)
    {
        request.EnsureUser();
        var (permissionModel, invalidBody) = await request.ReadJsonBodyAsync<Permission>(_bodyJsonOptions, "a permission", RequestBodyLimits.Control);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (!TryValidatePermission(permissionModel, out var badRequestResult))
        {
            return badRequestResult;
        }

        var accountId = request.GetIdentityUniqueId();
        permissionModel = permissionModel with { CreatedById = accountId };
        await _accessService.GrantPermissionAsync(permissionModel);
        var point = await _accessService.GetAccessPointAsync(permissionModel.AccessPointKey);
        return new OkObjectResult(point);
    }

    [Function(nameof(PostRevokePermission))]
    [OpenApiOperation(Definitions.RouteIds.Access.RevokeUserAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(Permission), Description = "The requested permission to revoke.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessPoint), Description = "The affected access point (organization or project).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostRevokePermission(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.Revoke)]
        HttpRequestData request)
    {
        request.EnsureUser();
        var (permissionModel, invalidBody) = await request.ReadJsonBodyAsync<Permission>(_bodyJsonOptions, "a permission", RequestBodyLimits.Control);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (!TryValidatePermission(permissionModel, out var badRequestResult))
        {
            return badRequestResult;
        }

        var accountId = request.GetIdentityUniqueId();
        permissionModel = permissionModel with { CreatedById = accountId };
        await _accessService.RevokePermissionAsync(permissionModel);
        var point = await _accessService.GetAccessPointAsync(permissionModel.AccessPointKey);
        return new OkObjectResult(point);
    }

    [Function(nameof(GetMachines))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetMachineAccesses, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<MachineAccess>), Description = "The list of machine accesses for the project.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetMachines(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.Machines)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await request.CheckUserAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        var machineAccesses = await _accessService.GetMachineAccessesAsync(organization, project);
        return new OkObjectResult(machineAccesses
            .Select(machineAccess => FederationHttp.WithTokenEndpoint(machineAccess, request, _federationOptions, organization, project))
            .ToList());
    }

    [Function(nameof(GetMachine))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetMachineAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdMachine)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineAccess), Description = "The machine access.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.Machine)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        await request.CheckUserAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        if (!MachineIds.IsValid(id))
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = Definitions.Errors.InvalidMachineId });
        }

        var machineAccess = await _accessService.GetMachineAccessAsync(organization, project, id);

        return machineAccess is null
            ? new NotFoundResult()
            : new OkObjectResult(FederationHttp.WithTokenEndpoint(machineAccess, request, _federationOptions, organization, project));
    }

    [Function(nameof(PostMachines))]
    [OpenApiOperation(Definitions.RouteIds.Access.CreateMachineAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(MachineAccessCreate), Description = "An organization or project to create.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineAccess), Description = "The created access point (organization or project).")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequestMachineAccessCreate)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenMachineScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostMachines(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.Machines)]
        HttpRequestData request,
        string organization,
        string project)
    {
        await request.CheckUserAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);
        var (machineModel, invalidBody) = await request.ReadJsonBodyAsync<MachineAccessCreate>(_bodyJsonOptions, "a machine", RequestBodyLimits.FederationRules);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (machineModel is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("The machine is missing."));
        }

        // The annotation restriction is stored in its canonical form; blank means the whole project.
        if (!MachineAnnotationRestriction.TryNormalize(machineModel.AnnotationKey, out var annotationKey))
        {
            return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {machineModel.AnnotationKey}") { ErrorCode = ErrorCodes.InvalidKey });
        }

        // The JSON reads integers as enum values too, so [99] gets here.
        if (MachineScopes.FindUndefined(machineModel.Scopes) is { } undefined)
        {
            return new BadRequestObjectResult(new ErrorResponse($"The machine scope {undefined:D} doesn't exist."));
        }

        // Write implies read, completed once, here: the stored list holds both, so every check needs one scope.
        var scopes = MachineScopes.WithImpliedReads(machineModel.Scopes);

        // A missing list, an empty one and management scopes alone are refused: a management scope grants nothing
        // without the runtime scope of its operation.
        if (!scopes.Any(MachineScopes.IsRuntime))
        {
            return new BadRequestObjectResult(new ErrorResponse("A machine needs at least one runtime scope: AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead or SecretsWrite."));
        }

        // Secrets scopes need ContributorWithSecrets and management scopes an Administrator; a Contributor gives the rest.
        await EnsureRoleForMachineScopesAsync(request, _accessService, organization, project, scopes);

        // TODO: [P1] hide the model from the client
        machineModel = machineModel with
        {
            Organization = organization,
            Project = project,
            AnnotationKey = annotationKey,
            Scopes = scopes,
        };

        try
        {
            var machineAccess = await _accessService.CreateMachineAccessAsync(machineModel);
            return new OkObjectResult(FederationHttp.WithTokenEndpoint(machineAccess, request, _federationOptions, organization, project));
        }
        catch (MachineAccessNotSupportedException exception)
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = exception.Message });
        }
    }

    [Function(nameof(PutMachine))]
    [OpenApiOperation(Definitions.RouteIds.Access.ResetMachineAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdMachine)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineAccess), Description = "The restarted machine access.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenMachineScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PutMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.Access.V1.Machine)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        await request.CheckUserAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        if (!MachineIds.IsValid(id))
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = Definitions.Errors.InvalidMachineId });
        }

        // A new secret gives out the machine's scopes, so the reset needs the role they need.
        await EnsureRoleForMachineScopesAsync(request, _accessService, organization, project, id);

        try
        {
            var machineAccess = await _accessService.ResetMachineAccessAsync(organization, project, id);
            return new OkObjectResult(machineAccess);
        }
        catch (MachineAccessNotSupportedException exception)
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = exception.Message });
        }
    }

    [Function(nameof(DeleteMachine))]
    [OpenApiOperation(Definitions.RouteIds.Access.DeleteMachineAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdMachine)]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The machine access doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenMachineScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Access.V1.Machine)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        await request.CheckUserAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        if (!MachineIds.IsValid(id))
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = Definitions.Errors.InvalidMachineId });
        }

        // Deleting a machine stops whatever runs with its scopes, so it needs the role they need, as the reset does.
        await EnsureRoleForMachineScopesAsync(request, _accessService, organization, project, id);

        bool isSuccess;

        try
        {
            isSuccess = await _accessService.DeleteMachineAccessAsync(organization, project, id);
        }
        catch (MachineAccessNotSupportedException exception)
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = exception.Message });
        }

        return isSuccess
            ? new OkResult()
            : new NotFoundResult();
    }

    [Function(nameof(GetCertificateAuthority))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetCertificateAuthority, Definitions.Tags.Access)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/x-pem-file", typeof(string), Description = "The CA public certificate in PEM format.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetCertificateAuthority(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.CertificateAuthority)]
        HttpRequestData request)
    {
        // This endpoint is unauthenticated — the CA public certificate is not a secret.
        if (_caProvider is null)
        {
            return new NotFoundResult();
        }

        var material = await _caProvider.GetActiveAsync();
        var pem = material.Certificate.ExportCertificatePem();
        return new ContentResult { Content = pem, ContentType = "application/x-pem-file", StatusCode = 200 };
    }

    [Function(nameof(GetMachineAuthentication))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetMachineAuthentication, Definitions.Tags.Access, Description = "The machine credential kinds the project enables, the default kind and the default certificate lifetime of new machines. Every property is filled; without a stored policy the deployment defaults apply.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The key of the access point (organization.project).")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineAuthenticationPolicy), Description = "The machine authentication policy of the project.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key is not organization.project.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetMachineAuthentication(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.MachineAuthentication)]
        HttpRequestData request,
        string key)
    {
        var pointKey = key.Trim().ToLowerInvariant();

        // Contributors create machines, so they read the kinds and the default lifetime too.
        await request.CheckUserAccessToAsync(_accessService, pointKey, AccountRole.Contributor);

        if (!TrySplitProjectKey(pointKey, out var organization, out var project, out var badRequestResult))
        {
            return badRequestResult;
        }

        var stored = _policyStore is null ? null : await _policyStore.GetAsync(organization, project);
        return new OkObjectResult(MachineAuthenticationPolicies.Resolve(stored, _machineOptions));
    }

    [Function(nameof(PutMachineAuthentication))]
    [OpenApiOperation(Definitions.RouteIds.Access.SetMachineAuthentication, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The key of the access point (organization.project).")]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(MachineAuthenticationPolicy), Description = "The machine authentication policy to set: the enabled credential kinds, the default kind (one of them; the first enabled kind when null) and the default certificate lifetime (the deployment default when null). A policy with only CredentialKind enables that one kind.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineAuthenticationPolicy), Description = "The updated machine authentication policy, with every property filled.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "No kind is enabled, a kind doesn't exist, the default kind is not enabled, or the certificate lifetime is out of the deployment's bounds.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PutMachineAuthentication(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.Access.V1.MachineAuthentication)]
        HttpRequestData request,
        string key)
    {
        var pointKey = key.Trim().ToLowerInvariant();
        await request.CheckUserAccessToAsync(_accessService, pointKey, AccountRole.Administrator);

        if (_policyStore is null)
        {
            return new StatusCodeResult((int)HttpStatusCode.NotImplemented);
        }

        if (!TrySplitProjectKey(pointKey, out var organization, out var project, out var badRequestResult))
        {
            return badRequestResult;
        }

        var (policy, invalidBody) = await request.ReadJsonBodyAsync<MachineAuthenticationPolicy>(_bodyJsonOptions, "a machine authentication policy", RequestBodyLimits.Control);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (MachineAuthenticationPolicies.Validate(policy, _machineOptions) is { } invalid)
        {
            return new BadRequestObjectResult(new ErrorResponse(invalid));
        }

        // Validate refuses a missing policy, so there is one here.
        var normalized = MachineAuthenticationPolicies.Normalize(policy!);
        await _policyStore.SetAsync(organization, project, normalized);
        return new OkObjectResult(MachineAuthenticationPolicies.Resolve(normalized, _machineOptions));
    }

    // The creator of a machine can use its credentials, so whatever gives them out or redirects them (reset, the
    // federation rules) needs the role the stored scopes need, and so does the delete, which takes them away from what
    // runs with them. One point read; a missing machine is left to the route, which answers as before.
    internal static async Task EnsureRoleForMachineScopesAsync(
        HttpRequestData request,
        IAccessService accessService,
        string organization,
        string project,
        string id)
    {
        var machine = await accessService.GetMachineAccessAsync(organization, project, id);

        if (machine is not null)
        {
            await EnsureRoleForMachineScopesAsync(request, accessService, organization, project, machine.Scopes);
        }
    }

    // A machine's scopes may not exceed what its creator may do or grant: a management scope needs an Administrator,
    // a secrets scope ContributorWithSecrets, and the Contributor check of the route covers the rest. A user role
    // check, so Debug builds (DEV_AUTH) skip it.
    internal static async Task EnsureRoleForMachineScopesAsync(
        HttpRequestData request,
        IAccessService accessService,
        string organization,
        string project,
        IReadOnlyList<MachineScope> scopes)
    {
        var role = GetRoleForMachineScopes(scopes);

        if (role > AccountRole.Contributor)
        {
            await request.CheckUserAccessToProjectAsync(accessService, organization, project, role);
        }
    }

    private static AccountRole GetRoleForMachineScopes(IReadOnlyList<MachineScope> scopes)
    {
        if (scopes.Any(MachineScopes.IsManagement))
        {
            return AccountRole.Administrator;
        }

        return scopes.Any(MachineScopes.IsSecrets)
            ? AccountRole.ContributorWithSecrets
            : AccountRole.Contributor;
    }

    private static bool TrySplitProjectKey(
        string pointKey,
        out string organization,
        out string project,
        [NotNullWhen(false)] out IActionResult? badRequestResult)
    {
        var segments = pointKey.Split('.', 2);

        if (segments.Length < 2)
        {
            organization = project = string.Empty;
            badRequestResult = new BadRequestObjectResult(new ErrorResponse { Message = "Access point key must be in organization.project format." });
            return false;
        }

        organization = segments[0];
        project = segments[1];
        badRequestResult = null;
        return true;
    }

    private static bool TryValidatePermission([NotNullWhen(true)] Permission? model, [NotNullWhen(false)] out IActionResult? badRequestResult)
    {
        var message = model switch
        {
            null => "The permission is missing.",
            { Role: AccountRole.None } => "The role None can't be granted or revoked.",
            { AccountId: var targetId } when string.IsNullOrWhiteSpace(targetId) => "The target account is missing.",
            { AccessPointKey: var pointKey } when string.IsNullOrWhiteSpace(pointKey) => "The access point key is missing.",
            _ => null,
        };

        badRequestResult = message is null
            ? null
            : new BadRequestObjectResult(new ErrorResponse(message));
        return message is null;
    }
}
