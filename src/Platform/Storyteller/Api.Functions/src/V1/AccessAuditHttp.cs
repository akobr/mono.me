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

namespace _42.Platform.Storyteller.Api.V1;

// The access audit trail of an organization or a project: who changed members, access points, machines, policies,
// trusted issuers and shared certificates. Only a read: the records are written by the changes themselves.
public class AccessAuditHttp
{
    private readonly IAccessService _accessService;
    private readonly IAccessAuditLog _audit;

    public AccessAuditHttp(IAccessService accessService, IAccessAuditLog audit)
    {
        _accessService = accessService;
        _audit = audit;
    }

    [Function(nameof(GetAccessAudit))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetAccessAudit, Definitions.Tags.Access, Description = "The access audit trail, newest first: every change of members, access points, machines, the machine authentication and key/value policies, trusted issuers and shared certificates, with the actor (and Via, the actor that acted for the person, with ViaClient, the client app the person used with it), the target, the value before and after, and the correlation id. Never a secret. With an organization key, the whole trail of the organization for its Owners and Administrators; with organization.project, the records of the project for its Owners and Administrators. Users only.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AccessPointKey)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only the records of this project; the organization's own records (its members) are left out. With a project key, only that project.")]
    [OpenApiParameter(Definitions.Parameters.Action, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only this action (MemberGranted, MemberRevoked, MemberRoleChanged, MemberRemoved, MemberJoined, AccessPointCreated, AccessPointUpdated, MachineCreated, MachineReset, MachineDeleted, MachineFederationRulesChanged, MachineAuthenticationPolicyChanged, KeyValuePolicyChanged, FederatedIssuerCreated, FederatedIssuerUpdated, FederatedIssuerDeleted, SharedCertificateIssued, SharedCertificateRevoked).")]
    [OpenApiParameter(Definitions.Parameters.Target, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only the records of this target: an account id, a machine id, a trusted issuer's name or a shared certificate's thumbprint.")]
    [OpenApiParameter(Definitions.Parameters.From, In = ParameterLocation.Query, Required = false, Type = typeof(DateTimeOffset), Description = "Only records at or after this time.")]
    [OpenApiParameter(Definitions.Parameters.To, In = ParameterLocation.Query, Required = false, Type = typeof(DateTimeOffset), Description = "Only records at or before this time.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AccessAuditResponse), Description = "One page of records, newest first, at most 100.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "A filter does not parse, the project of the query is not the project of the key, or the continuation token is not one of this trail (InvalidQuery).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAccessAudit(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.AccessAudit)]
        HttpRequestData request,
        string key)
    {
        request.EnsureUser("Reading the access audit needs a user with the Administrator role.");
        var pointKey = key.Trim().ToLowerInvariant();

        // The exact role on the key: the organization's trail needs the organization's role.
        await request.CheckUserAccessToAsync(_accessService, pointKey, AccountRole.Administrator);

        var segments = pointKey.Split('.', 2);
        var keyProject = segments.Length == 2 ? segments[1] : null;
        var queryProject = request.Query[Definitions.Parameters.Project]?.Trim().ToLowerInvariant();
        queryProject = string.IsNullOrEmpty(queryProject) ? null : queryProject;

        if (keyProject is not null && queryProject is not null && !string.Equals(keyProject, queryProject, StringComparison.Ordinal))
        {
            throw new InvalidInputException(
                $"The project '{queryProject}' of the query is not the project of the key '{pointKey}'.",
                ErrorCodes.InvalidQuery);
        }

        var query = new AccessAuditQuery
        {
            Project = keyProject ?? queryProject,
            Action = KeyValueQuery.GetEnum<AccessAuditAction>(request, Definitions.Parameters.Action),
            Target = string.IsNullOrWhiteSpace(request.Query[Definitions.Parameters.Target]) ? null : request.Query[Definitions.Parameters.Target]!.Trim(),
            From = KeyValueQuery.GetTime(request, Definitions.Parameters.From),
            To = KeyValueQuery.GetTime(request, Definitions.Parameters.To),
            ContinuationToken = request.Query[Definitions.Parameters.ContinuationToken],
        };

        if (query.From > query.To)
        {
            throw new InvalidInputException("The query parameter 'from' must not be later than 'to'.", ErrorCodes.InvalidQuery);
        }

        var page = await _audit.QueryAsync(segments[0], query, request.FunctionContext.CancellationToken);
        return new OkObjectResult(page);
    }
}
