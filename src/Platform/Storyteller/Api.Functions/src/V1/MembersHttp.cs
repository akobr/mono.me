using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace _42.Platform.Storyteller.Api.V1;

// Members of an organization or project. The service checks the caller's role on the access point,
// so 403 (AccessDenied), 404 (NotFound, MemberNotFound) and 409 (LastOwner, SelfRoleChange) come from the middleware.
public class MembersHttp
{
    private readonly IAccessService _accessService;

    public MembersHttp(IAccessService accessService)
    {
        _accessService = accessService;
    }

    [Function(nameof(GetMembers))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetMembers, Definitions.Tags.Access, Description = "Members of the organization or project, with their names. Needs Administrator.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccessPointKey)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<AccessPointMember>), Description = "The members, owners first.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The access point doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetMembers(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.Members)]
        HttpRequestData request,
        string key)
    {
        request.CheckScope(Scopes.User.Impersonation);
        var members = await _accessService.GetMembersAsync(NormalizeKey(key), request.GetIdentityUniqueId());
        return new OkObjectResult(members);
    }

    [Function(nameof(SetMemberRole))]
    [OpenApiOperation(Definitions.RouteIds.Access.SetMemberRole, Definitions.Tags.Access, Description = "Sets the exact role of a member, up or down. Needs Administrator; Owner to set or change Owner.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccessPointKey)]
    [OpenApiParameter(Definitions.Parameters.AccountId, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccountId)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(MemberRoleUpdate), Description = "The new role. None is not allowed; remove the member instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessPointMember), Description = "The member with the new role.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The access point doesn't exist, or the account is not a member (MemberNotFound).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The last owner can't be demoted (LastOwner), and members can't change their own role (SelfRoleChange).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetMemberRole(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.Access.V1.Member)]
        HttpRequestData request,
        [FromBody] MemberRoleUpdate? update,
        string key,
        string accountId)
    {
        request.CheckScope(Scopes.User.Impersonation);

        if (update is null || update.Role == AccountRole.None || !Enum.IsDefined(update.Role))
        {
            return new BadRequestObjectResult(new ErrorResponse("A role other than None is required; remove the member instead."));
        }

        var member = await _accessService.SetMemberRoleAsync(NormalizeKey(key), accountId, update.Role, request.GetIdentityUniqueId());
        return new OkObjectResult(member);
    }

    [Function(nameof(RemoveMember))]
    [OpenApiOperation(Definitions.RouteIds.Access.RemoveMember, Definitions.Tags.Access, Description = "Removes a member. Needs Administrator (Owner to remove an Owner); any member may remove themselves.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccessPointKey)]
    [OpenApiParameter(Definitions.Parameters.AccountId, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccountId)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent, Description = "The member was removed.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The access point doesn't exist, or the account is not a member (MemberNotFound).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The last owner can't be removed (LastOwner).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + Scopes.User.Impersonation)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> RemoveMember(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Access.V1.Member)]
        HttpRequestData request,
        string key,
        string accountId)
    {
        request.CheckScope(Scopes.User.Impersonation);
        await _accessService.RemoveMemberAsync(NormalizeKey(key), accountId, request.GetIdentityUniqueId());
        return new NoContentResult();
    }

    // Access point keys are stored lower-case, the same way GetAccessPoint reads them.
    internal static string NormalizeKey(string key)
    {
        return key.Trim().ToLowerInvariant();
    }
}
