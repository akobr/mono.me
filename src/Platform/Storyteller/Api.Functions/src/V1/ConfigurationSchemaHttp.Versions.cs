using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Configuring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

public partial class ConfigurationSchemaHttp
{
    private const string SchemaReadScopes = $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}";

    [Function(nameof(GetSchemaVersions))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetSchemaVersions, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<ConfigurationVersion>), Description = "The list of type-schema versions.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetSchemaVersions(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaTypeVersions)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        return ReadVersionsAsync(request, organization, project, annotationType, null, () => _schemaService.GetSchemaVersionsAsync(organization, project, view, annotationType));
    }

    [Function(nameof(GetSchemaVersion))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetSchemaVersion, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The type schema of the version.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested version doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetSchemaVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaTypeVersion)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType,
        uint version)
    {
        return ReadVersionAsync(request, organization, project, annotationType, null, () => _schemaService.GetSchemaVersionContentAsync(organization, project, view, annotationType, version));
    }

    [Function(nameof(GetSchemaVersionDiff))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetSchemaVersionDiff, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between this and the previous version. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The requested version doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetSchemaVersionDiff(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaTypeVersionDiff)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType,
        uint version)
    {
        return ReadDiffAsync(request, organization, project, annotationType, null, () => _schemaService.GetSchemaVersionChangesAsync(organization, project, view, annotationType, version));
    }

    [Function(nameof(GetSchemaVersionDiffCustom))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetSchemaVersionDiffCustom, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The target version number (to).")]
    [OpenApiParameter(Definitions.Parameters.VersionFrom, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The source version number (from).")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between the two specified versions. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "One of the requested versions doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetSchemaVersionDiffCustom(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaTypeVersionDiffCustom)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType,
        uint version,
        uint versionFrom)
    {
        return ReadDiffAsync(request, organization, project, annotationType, null, () => _schemaService.GetSchemaVersionChangesAsync(organization, project, view, annotationType, versionFrom, version));
    }

    [Function(nameof(GetAnnotationSchemaVersions))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchemaVersions, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<ConfigurationVersion>), Description = "The list of annotation-schema versions.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetAnnotationSchemaVersions(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaAnnotationVersions)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        return ReadVersionsAsync(request, organization, project, null, key, () => _schemaService.GetAnnotationSchemaVersionsAsync(organization, project, view, key));
    }

    [Function(nameof(GetAnnotationSchemaVersion))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchemaVersion, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The annotation schema of the version.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested version doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetAnnotationSchemaVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaAnnotationVersion)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version)
    {
        return ReadVersionAsync(request, organization, project, null, key, () => _schemaService.GetAnnotationSchemaVersionContentAsync(organization, project, view, key, version));
    }

    [Function(nameof(GetAnnotationSchemaVersionDiff))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchemaVersionDiff, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between this and the previous version. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The requested version doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetAnnotationSchemaVersionDiff(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaAnnotationVersionDiff)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version)
    {
        return ReadDiffAsync(request, organization, project, null, key, () => _schemaService.GetAnnotationSchemaVersionChangesAsync(organization, project, view, key, version));
    }

    [Function(nameof(GetAnnotationSchemaVersionDiffCustom))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchemaVersionDiffCustom, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The target version number (to).")]
    [OpenApiParameter(Definitions.Parameters.VersionFrom, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The source version number (from).")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between the two specified versions. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "One of the requested versions doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetAnnotationSchemaVersionDiffCustom(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaAnnotationVersionDiffCustom)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version,
        uint versionFrom)
    {
        return ReadDiffAsync(request, organization, project, null, key, () => _schemaService.GetAnnotationSchemaVersionChangesAsync(organization, project, view, key, versionFrom, version));
    }

    [Function(nameof(GetDescendantTypeSchemaVersions))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchemaVersions, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The descendant annotation type code.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<ConfigurationVersion>), Description = "The list of descendant-type schema versions.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetDescendantTypeSchemaVersions(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaDescendantTypeVersions)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string annotationType)
    {
        return ReadVersionsAsync(request, organization, project, annotationType, key, () => _schemaService.GetDescendantTypeSchemaVersionsAsync(organization, project, view, key, annotationType));
    }

    [Function(nameof(GetDescendantTypeSchemaVersion))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchemaVersion, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The descendant annotation type code.")]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The descendant-type schema of the version.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested version doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetDescendantTypeSchemaVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaDescendantTypeVersion)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string annotationType,
        uint version)
    {
        return ReadVersionAsync(request, organization, project, annotationType, key, () => _schemaService.GetDescendantTypeSchemaVersionContentAsync(organization, project, view, key, annotationType, version));
    }

    [Function(nameof(GetDescendantTypeSchemaVersionDiff))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchemaVersionDiff, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The descendant annotation type code.")]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between this and the previous version. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The requested version doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetDescendantTypeSchemaVersionDiff(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaDescendantTypeVersionDiff)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string annotationType,
        uint version)
    {
        return ReadDiffAsync(request, organization, project, annotationType, key, () => _schemaService.GetDescendantTypeSchemaVersionChangesAsync(organization, project, view, key, annotationType, version));
    }

    [Function(nameof(GetDescendantTypeSchemaVersionDiffCustom))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchemaVersionDiffCustom, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The descendant annotation type code.")]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The target version number (to).")]
    [OpenApiParameter(Definitions.Parameters.VersionFrom, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The source version number (from).")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between the two specified versions. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "One of the requested versions doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + SchemaReadScopes)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> GetDescendantTypeSchemaVersionDiffCustom(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaDescendantTypeVersionDiffCustom)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string annotationType,
        uint version,
        uint versionFrom)
    {
        return ReadDiffAsync(
            request,
            organization,
            project,
            annotationType,
            key,
            () => _schemaService.GetDescendantTypeSchemaVersionChangesAsync(organization, project, view, key, annotationType, versionFrom, version));
    }

    private async Task<IActionResult> ReadVersionsAsync(
        HttpRequestData request,
        string organization,
        string project,
        string? annotationType,
        string? annotationKey,
        Func<Task<IReadOnlyCollection<ConfigurationVersion>>> read)
    {
        var rejected = await PrepareSchemaReadAsync(request, organization, project, annotationType, annotationKey);
        return rejected ?? new OkObjectResult(await read());
    }

    private async Task<IActionResult> ReadVersionAsync(
        HttpRequestData request,
        string organization,
        string project,
        string? annotationType,
        string? annotationKey,
        Func<Task<ConfigurationSchema?>> read)
    {
        var rejected = await PrepareSchemaReadAsync(request, organization, project, annotationType, annotationKey);

        if (rejected is not null)
        {
            return rejected;
        }

        var schema = await read();
        return schema is null ? new NotFoundResult() : new OkObjectResult(schema);
    }

    private async Task<IActionResult> ReadDiffAsync(
        HttpRequestData request,
        string organization,
        string project,
        string? annotationType,
        string? annotationKey,
        Func<Task<DiffResult>> read)
    {
        var rejected = await PrepareSchemaReadAsync(request, organization, project, annotationType, annotationKey);
        return rejected ?? await read().ToDiffResponseAsync(request);
    }

    private async Task<IActionResult?> PrepareSchemaReadAsync(
        HttpRequestData request,
        string organization,
        string project,
        string? annotationType,
        string? annotationKey)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (annotationType is not null && !TryValidateAnnotationType(annotationType, out var rejected))
        {
            return rejected;
        }

        if (annotationKey is not null && !TryValidateAnnotationKey(annotationKey, out rejected))
        {
            return rejected;
        }

        return null;
    }
}
