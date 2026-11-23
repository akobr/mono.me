using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Notifying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

// The inbox of a view: the results of background jobs, what the platform suggests and plain messages, which people
// accept or dismiss. The routes are for users only, with the Contributor role on the project: machines have no role, and
// no scope gives them the inbox. The role is not only checked but handed to the service, because it also decides which
// items there are for the caller: one above it is not found, whatever is asked of it. The view is not checked, as on the
// other routes of a view: the inbox of a view without items is empty.
public class ViewInboxHttp
{
    private readonly IViewInboxService _inbox;
    private readonly IAccessService _access;

    public ViewInboxHttp(IViewInboxService inbox, IAccessService access)
    {
        _inbox = inbox;
        _access = access;
    }

    [Function(nameof(GetViewInbox))]
    [OpenApiOperation(Definitions.RouteIds.Inbox.GetViewInbox, Definitions.Tags.Inbox, Description = "The inbox of the view, newest first, 50 items a page: the results of background jobs such as exports and imports (Notification), what the platform suggests (Action) and plain messages (Message). An item is open for everybody who sees it until one of them accepts or dismisses it. Only the items the caller's role on the project may see are listed and counted (MinimalRole); OpenCount is every open one of them, whatever the page and the state. The inbox of a view without items is empty. Users only, with the Contributor role.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.State, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Open (default), or Done: the accepted and the dismissed items.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ViewInboxResponse), Description = "One page of items, newest first, at most 50, and the count of the open items.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The state is not Open or Done, or the continuation token is not one that the inbox issues (ErrorCode InvalidQuery). A token of the inbox of another view is not refused: it pages this view below the item it names, so start again without a token after a change of the view.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenViewInbox)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetViewInbox(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Inbox.V1.Inbox)]
        HttpRequestData request,
        string organization,
        string project,
        string view)
    {
        var role = await request.GetUserRoleInProjectAsync(_access, organization, project, AccountRole.Contributor);

        var query = new ViewInboxQuery
        {
            Filter = KeyValueQuery.GetEnum<ViewInboxFilter>(request, Definitions.Parameters.State) ?? ViewInboxFilter.Open,
            Role = role,

            // as it came: the service decodes it and refuses one it did not issue
            ContinuationToken = request.Query[Definitions.Parameters.ContinuationToken],
        };

        var page = await _inbox.GetItemsAsync(organization, project, view, query, request.FunctionContext.CancellationToken);
        return new OkObjectResult(page);
    }

    [Function(nameof(AcceptViewInboxItem))]
    [OpenApiOperation(Definitions.RouteIds.Inbox.AcceptViewInboxItem, Definitions.Tags.Inbox, Description = "Accepts an open action of the inbox. The item becomes Accepted for everybody who sees it, stamped with the caller, and what the action stands for is started: typically a background job, named by Job, whose result arrives as a notification of its own. Only an item of the kind Action can be accepted. No request body. Users only, with the Contributor role and the role the item needs (MinimalRole).")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdInboxItem)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ViewInboxItem), Description = "The accepted item, with the job its acceptance started when there is one.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotFoundInboxItem)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The item is not an action (ErrorCode InboxItemNotAction), or it was already accepted or dismissed (ErrorCode InboxItemClosed). The item changed concurrently too many times (ErrorCode Conflict); try again. What the action starts could not start: the error code is the one of that work, such as TransferLimit, and the item is opened again on a best effort, so read it again before you offer another try.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenViewInbox)]
    [OpenApiResponseWithBody(HttpStatusCode.NotImplemented, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The deployment has nothing that runs the action of the item (ErrorCode InboxActionNotAvailable); the item stays open.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> AcceptViewInboxItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Inbox.V1.ItemAccept)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string id)
    {
        var role = await request.GetUserRoleInProjectAsync(_access, organization, project, AccountRole.Contributor);

        // the audit actor carries Via, so an acceptance through an actor (the MCP server) is stamped as one
        var item = await _inbox.AcceptAsync(organization, project, view, id, role, request.GetAuditActor(), request.FunctionContext.CancellationToken);
        return new OkObjectResult(item);
    }

    [Function(nameof(DismissViewInboxItem))]
    [OpenApiOperation(Definitions.RouteIds.Inbox.DismissViewInboxItem, Definitions.Tags.Inbox, Description = "Dismisses an open item of the inbox, of any kind. The item becomes Dismissed for everybody who sees it, stamped with the caller, and stays among the done items until it expires; nothing is started. No request body. Users only, with the Contributor role and the role the item needs (MinimalRole).")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Id, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.IdInboxItem)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ViewInboxItem), Description = "The dismissed item.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotFoundInboxItem)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The item was already accepted or dismissed (ErrorCode InboxItemClosed). The item changed concurrently too many times (ErrorCode Conflict); try again.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenViewInbox)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DismissViewInboxItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Inbox.V1.ItemDismiss)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string id)
    {
        var role = await request.GetUserRoleInProjectAsync(_access, organization, project, AccountRole.Contributor);

        var item = await _inbox.DismissAsync(organization, project, view, id, role, request.GetAuditActor(), request.FunctionContext.CancellationToken);
        return new OkObjectResult(item);
    }
}
