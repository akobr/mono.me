using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.ErrorHandling;
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

// The restores of a schema version: the access of the schema's write, then the service.
public partial class ConfigurationSchemaHttp
{
    [Function(nameof(RestoreSchemaVersion))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.RestoreSchemaVersion, Definitions.Tags.Schemas, Description = "Stores the content of a version of the type schema as its new version, also after the schema was deleted. The version must be a usable schema, and the stored configurations of the type in the view must comply with it unless force is true. The new version records RestoredFrom.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = Definitions.Descriptions.RestoreVersion)]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.ForceSchemaRestore)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(VersionRestoreResult), Description = Definitions.Descriptions.ResponseVersionRestored)]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseSchemaVersionRestoreBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseVersionRestoreNotFound)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(SchemaValidationErrorResponse), Description = Definitions.Descriptions.ResponseSchemaVersionRestoreConflict)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenManagementSchemaWrite)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> RestoreSchemaVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaTypeVersionRestore)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType,
        uint version)
    {
        return RestoreVersionAsync(
            request,
            organization,
            project,
            annotationType,
            null,
            () => _schemaService.RestoreSchemaVersionAsync(organization, project, view, annotationType, version, request.GetAuditActor(), IsForce(request)));
    }

    [Function(nameof(RestoreAnnotationSchemaVersion))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.RestoreAnnotationSchemaVersion, Definitions.Tags.Schemas, Description = "Stores the content of a version of the annotation's schema as its new version, also after the schema was deleted. The version must be a usable schema, and the annotation's stored configuration must comply with it unless force is true. The new version records RestoredFrom. A deleted annotation answers 409 (restore it first), a missing or purged one 404.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = Definitions.Descriptions.RestoreVersion)]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.ForceSchemaRestore)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(VersionRestoreResult), Description = Definitions.Descriptions.ResponseVersionRestored)]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseSchemaVersionRestoreBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseAnnotationVersionRestoreNotFound)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(SchemaValidationErrorResponse), Description = Definitions.Descriptions.ResponseAnnotationSchemaVersionRestoreConflict)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenManagementSchemaWrite)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> RestoreAnnotationSchemaVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaAnnotationVersionRestore)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version)
    {
        return RestoreVersionAsync(
            request,
            organization,
            project,
            null,
            key,
            () => _schemaService.RestoreAnnotationSchemaVersionAsync(organization, project, view, key, version, request.GetAuditActor(), IsForce(request)));
    }

    [Function(nameof(RestoreDescendantTypeSchemaVersion))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.RestoreDescendantTypeSchemaVersion, Definitions.Tags.Schemas, Description = "Stores the content of a version of the descendant-type schema as its new version, also after the schema was deleted. The type must be able to descend from the key, the version must be a usable schema, and the stored configurations of the type under the key must comply with it unless force is true. The new version records RestoredFrom. A deleted annotation answers 409 (restore it first), a missing or purged one 404.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = Definitions.Descriptions.RestoreVersion)]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.ForceSchemaRestore)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(VersionRestoreResult), Description = Definitions.Descriptions.ResponseVersionRestored)]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseDescendantTypeSchemaVersionRestoreBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseAnnotationVersionRestoreNotFound)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(SchemaValidationErrorResponse), Description = Definitions.Descriptions.ResponseAnnotationSchemaVersionRestoreConflict)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenManagementSchemaWrite)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public Task<IActionResult> RestoreDescendantTypeSchemaVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaDescendantTypeVersionRestore)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string annotationType,
        uint version)
    {
        return RestoreVersionAsync(
            request,
            organization,
            project,
            annotationType,
            key,
            () => _schemaService.RestoreDescendantTypeSchemaVersionAsync(organization, project, view, key, annotationType, version, request.GetAuditActor(), IsForce(request)));
    }

    private async Task<IActionResult> RestoreVersionAsync(
        HttpRequestData request,
        string organization,
        string project,
        string? annotationType,
        string? annotationKey,
        Func<Task<VersionRestoreResult>> restore)
    {
        // as the schema's write: a type-level schema spans the project, the schemas of an annotation belong to it
        if (annotationKey is null)
        {
            await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor, MachineScope.ConfigurationWrite, MachineScope.SchemaWrite);
        }
        else
        {
            await request.CheckAccessToAnnotationAsync(_access, organization, project, annotationKey, AccountRole.Contributor, MachineScope.ConfigurationWrite, MachineScope.SchemaWrite);
        }

        if (annotationType is not null && !TryValidateAnnotationType(annotationType, out var rejected))
        {
            return rejected;
        }

        if (annotationKey is not null && !TryValidateAnnotationKey(annotationKey, out rejected))
        {
            return rejected;
        }

        try
        {
            return new OkObjectResult(await restore());
        }
        catch (StorytellerException ex)
        {
            // VersionNotFound and AnnotationNotFound 404, AnnotationDeleted 409, InvalidKey and an unusable schema 400
            return ErrorResponseMapping.ToActionResult(ex);
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new ErrorResponse(ex.Message));
        }
        catch (SchemaValidationException ex)
        {
            return ToConflictResult(ex);
        }
        catch (SchemaConcurrencyException ex)
        {
            return new ConflictObjectResult(new ErrorResponse(ex.Message) { ErrorCode = ErrorCodes.Conflict });
        }
    }
}
