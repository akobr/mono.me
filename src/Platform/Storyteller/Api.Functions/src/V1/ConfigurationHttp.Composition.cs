using System.Net;
using _42.Platform.Storyteller.Api.ConditionalReads;
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

// The composition of the effective configuration: which documents are merged, in which order, and which one set each value.
public partial class ConfigurationHttp
{
    [Function(nameof(GetConfigurationComposition))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurationComposition, Definitions.Tags.Configurations, Description = Definitions.Descriptions.ConfigurationComposition + " " + Definitions.Descriptions.ConditionalRead)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationComposition), CustomHeaderType = typeof(EntityTagResponseHeader), Description = "The composition of the effective configuration.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotModified, CustomHeaderType = typeof(EntityTagResponseHeader), Description = Definitions.Descriptions.ResponseNotModified)]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseConfigurationNotFound)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationComposition(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.ConfigurationComposition)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);

        // the whole project, as the templates: the ancestors' documents and the templates are shown in full, also the
        // values the annotation overrides, so a machine restricted to an annotation is refused even for its own one
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var composition = await _configuration.GetConfigurationCompositionAsync(fullKey);

        if (composition is null)
        {
            return await NotFoundAsync(fullKey);
        }

        request.MarkNoCache();
        return request.OkOrNotModified(composition, ConfigurationEntityTags.ForComposition(fullKey, composition));
    }
}
