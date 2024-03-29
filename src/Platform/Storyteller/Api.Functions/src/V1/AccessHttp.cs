using System.Net;
using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Access.Entities;
using _42.Platform.Storyteller.Access.Models;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace _42.Platform.Storyteller.Api.V1;

public class AccessHttp
{
    private readonly IAccessService _accessService;
    private readonly ILogger<AccessHttp> _logger;

    public AccessHttp(
        IAccessService accessService,
        ILogger<AccessHttp> logger)
    {
        _accessService = accessService;
        _logger = logger;
    }

    [Function(nameof(GetAccount))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAccount, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Account), Description = Definitions.Descriptions.ResponseAccount)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The account doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAccount(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.Account)]
        HttpRequestData request)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var accountKey = request.GetUniqueIdentityName().ToNormalizedKey();
        _logger.LogInformation("Get api/access/account call with accountKey: {accountKey}", accountKey);
        var account = await _accessService.GetAccountAsync(accountKey);

        return account is null
            ? new NotFoundResult()
            : new OkObjectResult(account);
    }

    [Function(nameof(PostAccount))]
    [OpenApiOperation(Definitions.RouteIds.Access.CreateAccount, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(Models.AccountCreate), Description = "Organization and project for the new account.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Account), Description = "Newly created account.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "The account already exists.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostAccount(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.Account)]
        HttpRequestData request,
        [FromBody] Models.AccountCreate accountModel)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var accountKey = request.GetUniqueIdentityName().ToNormalizedKey();
        _logger.LogInformation("Post api/access/account call with accountKey: {accountKey}", accountKey);
        var account = await _accessService.GetAccountAsync(accountKey);

        if (account is not null)
        {
            return new BadRequestResult();
        }

        var accountName = request.GetIdentityName().Trim();
        var internalAccountModel = new AccountCreate
        {
            Key = accountKey,
            Name = accountName,
            Organization = accountModel.Organization,
            Project = accountModel.Project,
        };
        account = await _accessService.CreateAccountAsync(internalAccountModel);

        return new OkObjectResult(account);
    }

    [Function(nameof(GetAccessPoints))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAccessPoints, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<AccessPoint>), Description = "The list of all manageable organizations and projects. The access points where the account is Admin or Owner.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAccessPoints(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.AccessPoints)]
        HttpRequestData request)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var accountKey = request.GetUniqueIdentityName().ToNormalizedKey();
        _logger.LogInformation("Post api/access/points call with accountKey: {accountKey}", accountKey);
        var points = await _accessService.GetAccessPointsAsync(accountKey);
        return new OkObjectResult(points);
    }

    [Function(nameof(GetAccessPoint))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAccessPoint, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The key of requested access point.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessPoint), Description = "The access point (organization or project).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAccessPoint(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.AccessPoint)]
        HttpRequestData request,
        string key)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var pointKey = key.Trim().ToLowerInvariant();
        await request.CheckAccessToAsync(_accessService, pointKey, AccountRole.Administrator);
        var point = await _accessService.GetAccessPointAsync(pointKey);
        return new OkObjectResult(point);
    }

    [Function(nameof(PostAccessPoints))]
    [OpenApiOperation(Definitions.RouteIds.Access.CreateAccessPoint, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(AccessPointCreate), Description = "An organization or project to create.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessPoint), Description = "The created access point (organization or project).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostAccessPoints(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.AccessPoints)]
        HttpRequestData request,
        [FromBody] AccessPointCreate pointModel)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var accountKey = request.GetUniqueIdentityName().ToNormalizedKey();
        _logger.LogInformation("Post api/access/points call with accountKey: {accountKey}", accountKey);
        pointModel = pointModel with { OwnerKey = accountKey };
        var point = await _accessService.CreateAccessPointAsync(pointModel);
        return new OkObjectResult(point);
    }

    [Function(nameof(PostGrantPermission))]
    [OpenApiOperation(Definitions.RouteIds.Access.GrantUserAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(Permission), Description = "The requested permission to grant.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessPoint), Description = "The affected access point (organization or project).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostGrantPermission(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.Grant)]
        HttpRequestData request,
        [FromBody] Permission permissionModel)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var accountKey = request.GetUniqueIdentityName().ToNormalizedKey();
        permissionModel = permissionModel with { CreatedByKey = accountKey };
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
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostRevokePermission(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.Revoke)]
        HttpRequestData request,
        [FromBody] Permission permissionModel)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var accountKey = request.GetUniqueIdentityName().ToNormalizedKey();
        permissionModel = permissionModel with { CreatedByKey = accountKey };
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
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetMachines(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.Machines)]
        HttpRequestData request,
        string organization,
        string project)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        var machineAccesses = await _accessService.GetMachineAccessesAsync(organization, project);
        return new OkObjectResult(machineAccesses);
    }

    [Function(nameof(GetMachine))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetMachineAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdMachine)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineAccess), Description = "The machine access.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.Machine)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        if (!Guid.TryParse(id, out var machineId))
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = Definitions.Errors.InvalidMachineId });
        }

        var machineAccess = await _accessService.GetMachineAccessAsync(organization, project, id);

        return machineAccess is null
            ? new NotFoundResult()
            : new OkObjectResult(machineAccess);
    }

    [Function(nameof(PostMachines))]
    [OpenApiOperation(Definitions.RouteIds.Access.CreateMachineAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(MachineAccessCreate), Description = "An organization or project to create.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineAccess), Description = "The created access point (organization or project).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PostMachines(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Access.V1.Machines)]
        HttpRequestData request,
        [FromBody] MachineAccessCreate machineModel,
        string organization,
        string project)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        machineModel = machineModel with { Organization = organization, Project = project };
        var machineAccess = await _accessService.CreateMachineAccessAsync(machineModel);
        return new OkObjectResult(machineAccess);
    }

    [Function(nameof(PutMachine))]
    [OpenApiOperation(Definitions.RouteIds.Access.ResetMachineAccess, Definitions.Tags.Access)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdMachine)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(MachineAccess), Description = "The restarted machine access.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PutMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.Access.V1.Machine)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        if (!Guid.TryParse(id, out _))
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = Definitions.Errors.InvalidMachineId });
        }

        var machineAccess = await _accessService.ResetMachineAccessAsync(organization, project, id);
        return new OkObjectResult(machineAccess);
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
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Access.V1.Machine)]
        HttpRequestData request,
        string organization,
        string project,
        string id)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await request.CheckAccessToProjectAsync(_accessService, organization, project, AccountRole.Contributor);

        if (!Guid.TryParse(id, out _))
        {
            return new BadRequestObjectResult(new ErrorResponse { Message = Definitions.Errors.InvalidMachineId });
        }

        var isSuccess = await _accessService.DeleteMachineAccessAsync(organization, project, id);

        return isSuccess
            ? new OkResult()
            : new NotFoundResult();
    }
}
