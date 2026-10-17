using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Binding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

// The binding functions and sources a deployment registered, for completion and help in clients. Registrations are
// global today; the route is project-scoped so it is guarded like every other read and can list project sources later.
public class BindingsHttp
{
    private readonly IBindingCatalog? _catalog;
    private readonly IAccessService _access;

    public BindingsHttp(IAccessService access, IBindingCatalog? catalog = null)
    {
        _access = access;
        _catalog = catalog;
    }

    [Function(nameof(GetBindingCatalog))]
    [OpenApiOperation(Definitions.RouteIds.Bindings.GetBindingCatalog, Definitions.Tags.Configurations, Description = "The binding functions and sources registered on the server, with descriptions and examples. Values are never resolved.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(BindingCatalog), Description = "The registered functions and sources, sorted by name.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetBindingCatalog(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Bindings.V1.Bindings)]
        HttpRequestData request,
        string organization,
        string project)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectMetadataAsync(_access, organization, project);

        return new OkObjectResult(_catalog?.GetCatalog() ?? new BindingCatalog());
    }
}
