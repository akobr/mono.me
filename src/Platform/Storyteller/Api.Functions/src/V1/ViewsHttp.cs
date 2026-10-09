using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
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

// The views of a project. Views stay implicit for writes; the registry adds descriptions and a cheap list.
public class ViewsHttp
{
    private readonly IViewService _views;
    private readonly IAccessService _access;

    public ViewsHttp(IViewService views, IAccessService access)
    {
        _views = views;
        _access = access;
    }

    [Function(nameof(GetViews))]
    [OpenApiOperation(Definitions.RouteIds.Views.GetViews, Definitions.Tags.Annotations, Description = "Registered views of the project and \"default\". With discover=true (Administrator), also views found in the project's data.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.Discover, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "Also scan the project's data for views that are not registered (IsRegistered = false). Needs Administrator.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<View>), Description = "The views, \"default\" first.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetViews(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Views.V1.Views)]
        HttpRequestData request,
        string organization,
        string project,
        [FromQuery] bool discover = false)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, discover ? AccountRole.Administrator : AccountRole.Reader);

        var views = await _views.GetViewsAsync(organization, project, discover);
        return new OkObjectResult(views);
    }

    [Function(nameof(CreateView))]
    [OpenApiOperation(Definitions.RouteIds.Views.CreateView, Definitions.Tags.Annotations, Description = "Registers a view with an optional description. Needs Administrator.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(ViewCreate), Description = "The view name (2 to 63 lower-case letters, digits or hyphens) and an optional description.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(View), Description = "The registered view.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The name is not valid or reserved (InvalidName).")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The view is already registered, or is \"default\" (ViewExists).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> CreateView(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Views.V1.Views)]
        HttpRequestData request,
        [FromBody] ViewCreate? model,
        string organization,
        string project)
    {
        request.CheckScope(Scopes.Annotation.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        if (model is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("The view is missing."));
        }

        var view = await _views.CreateViewAsync(organization, project, model, request.GetAuthor());
        return new OkObjectResult(view);
    }

    [Function(nameof(UpdateView))]
    [OpenApiOperation(Definitions.RouteIds.Views.UpdateView, Definitions.Tags.Annotations, Description = "Sets the description of a view and registers it when needed. Needs Administrator.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(ViewUpdate), Description = "The new description; empty clears it.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(View), Description = "The registered view.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The name is not valid or reserved (InvalidName).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> UpdateView(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.Views.V1.View)]
        HttpRequestData request,
        [FromBody] ViewUpdate? model,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(Scopes.Annotation.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        var updated = await _views.UpdateViewAsync(organization, project, view, model ?? new ViewUpdate(), request.GetAuthor());
        return new OkObjectResult(updated);
    }
}
