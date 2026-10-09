using System.Net;
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

public partial class ConfigurationSchemaHttp
{
    [Function(nameof(GetConfigurationSchemas))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetConfigurationSchemas, Definitions.Tags.Schemas, Description = "Every schema document of the view (type, annotation, descendant type) without its content.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<ConfigurationSchemaSummary>), Description = "The schema summaries: type schemas first, then annotation and descendant-type schemas.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationSchemas(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.Schemas)]
        HttpRequestData request,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var schemas = await _schemaService.ListSchemasAsync(organization, project, view);
        return new OkObjectResult(schemas);
    }
}
