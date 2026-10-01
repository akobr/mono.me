using System.Net;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Configuring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.V1;

public partial class ConfigurationSchemaHttp
{
    private readonly IConfigurationSchemaService _schemaService;
    private readonly IAccessService _access;
    private readonly ILogger<ConfigurationSchemaHttp> _logger;

    public ConfigurationSchemaHttp(
        IConfigurationSchemaService schemaService,
        IAccessService access,
        ILogger<ConfigurationSchemaHttp> logger)
    {
        _schemaService = schemaService;
        _access = access;
        _logger = logger;
    }

    [Function(nameof(GetConfigurationSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetConfigurationSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The configuration schema for the specified annotation type.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested configuration schema doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaType)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryValidateAnnotationType(annotationType, out var badRequestResult))
        {
            return badRequestResult;
        }

        var schema = await _schemaService.GetSchemaAsync(organization, project, view, annotationType);

        if (schema is null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(schema);
    }

    [Function(nameof(SetConfigurationSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.SetConfigurationSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "When true, save the schema even if stored configurations in the view do not comply.")]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(JObject), Description = "The JSON Schema to apply to configurations of this annotation type.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The created or updated configuration schema.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(SchemaValidationErrorResponse), Description = "Existing configurations are not compliant with the provided schema. A concurrent modification is returned as ErrorResponse.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetConfigurationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaType)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!TryValidateAnnotationType(annotationType, out var badRequestResult))
        {
            return badRequestResult;
        }

        JObject inputModel;
        try
        {
            using var sReader = new StreamReader(request.Body);
            await using var jReader = new JsonTextReader(sReader);
            inputModel = await JObject.LoadAsync(jReader);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error occurred while parsing input JSON schema.");
            return new BadRequestObjectResult(new ErrorResponse($"Invalid input JSON schema: {e.Message}"));
        }

        try
        {
            var author = request.GetAuthor();
            var schema = await _schemaService.SetSchemaAsync(organization, project, view, annotationType, inputModel, author, IsForce(request));
            return new OkObjectResult(schema);
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
            return new ConflictObjectResult(new ErrorResponse(ex.Message));
        }
    }

    [Function(nameof(DeleteConfigurationSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.DeleteConfigurationSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The configuration schema doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "A concurrent modification prevented the deletion.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteConfigurationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaType)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!TryValidateAnnotationType(annotationType, out var badRequestResult))
        {
            return badRequestResult;
        }

        try
        {
            var deleted = await _schemaService.DeleteSchemaAsync(organization, project, view, annotationType);
            return deleted ? new OkResult() : new NotFoundResult();
        }
        catch (SchemaConcurrencyException ex)
        {
            return new ConflictObjectResult(new ErrorResponse(ex.Message));
        }
    }

    [Function(nameof(GetAnnotationSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetAnnotationSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The annotation-level configuration schema.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested annotation schema doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAnnotationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaAnnotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryValidateAnnotationKey(key, out var badRequestResult))
        {
            return badRequestResult;
        }

        var schema = await _schemaService.GetAnnotationSchemaAsync(organization, project, view, key);

        if (schema is null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(schema);
    }

    [Function(nameof(SetAnnotationSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.SetAnnotationSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "When true, save the schema even if stored configurations in the view do not comply.")]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(JObject), Description = "The JSON Schema to apply to configurations of this annotation.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The created or updated annotation-level schema.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(SchemaValidationErrorResponse), Description = "Existing configurations are not compliant with the provided schema. A concurrent modification is returned as ErrorResponse.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetAnnotationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaAnnotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!TryValidateAnnotationKey(key, out var badRequestResult))
        {
            return badRequestResult;
        }

        JObject inputModel;
        try
        {
            using var sReader = new StreamReader(request.Body);
            await using var jReader = new JsonTextReader(sReader);
            inputModel = await JObject.LoadAsync(jReader);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error occurred while parsing input JSON schema.");
            return new BadRequestObjectResult(new ErrorResponse($"Invalid input JSON schema: {e.Message}"));
        }

        try
        {
            var author = request.GetAuthor();
            var schema = await _schemaService.SetAnnotationSchemaAsync(organization, project, view, key, inputModel, author, IsForce(request));
            return new OkObjectResult(schema);
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
            return new ConflictObjectResult(new ErrorResponse(ex.Message));
        }
    }

    [Function(nameof(DeleteAnnotationSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.DeleteAnnotationSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The annotation schema doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "A concurrent modification prevented the deletion.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteAnnotationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaAnnotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!TryValidateAnnotationKey(key, out var badRequestResult))
        {
            return badRequestResult;
        }

        try
        {
            var deleted = await _schemaService.DeleteAnnotationSchemaAsync(organization, project, view, key);
            return deleted ? new OkResult() : new NotFoundResult();
        }
        catch (SchemaConcurrencyException ex)
        {
            return new ConflictObjectResult(new ErrorResponse(ex.Message));
        }
    }

    [Function(nameof(GetDescendantTypeSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetDescendantTypeSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The descendant annotation type code.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The descendant-type configuration schema.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested descendant-type schema doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetDescendantTypeSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaDescendantType)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryValidateAnnotationKey(key, out var badRequestResult))
        {
            return badRequestResult;
        }

        if (!TryValidateAnnotationType(annotationType, out badRequestResult))
        {
            return badRequestResult;
        }

        var schema = await _schemaService.GetDescendantTypeSchemaAsync(organization, project, view, key, annotationType);

        if (schema is null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(schema);
    }

    [Function(nameof(SetDescendantTypeSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.SetDescendantTypeSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The descendant annotation type code.")]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "When true, save the schema even if stored configurations in the view do not comply.")]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(JObject), Description = "The JSON Schema to apply to descendant configurations of this type.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The created or updated descendant-type schema.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(SchemaValidationErrorResponse), Description = "Existing configurations are not compliant with the provided schema. A concurrent modification is returned as ErrorResponse.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetDescendantTypeSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaDescendantType)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!TryValidateAnnotationKey(key, out var badRequestResult))
        {
            return badRequestResult;
        }

        if (!TryValidateAnnotationType(annotationType, out badRequestResult))
        {
            return badRequestResult;
        }

        JObject inputModel;
        try
        {
            using var sReader = new StreamReader(request.Body);
            await using var jReader = new JsonTextReader(sReader);
            inputModel = await JObject.LoadAsync(jReader);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error occurred while parsing input JSON schema.");
            return new BadRequestObjectResult(new ErrorResponse($"Invalid input JSON schema: {e.Message}"));
        }

        try
        {
            var author = request.GetAuthor();
            var schema = await _schemaService.SetDescendantTypeSchemaAsync(organization, project, view, key, annotationType, inputModel, author, IsForce(request));
            return new OkObjectResult(schema);
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
            return new ConflictObjectResult(new ErrorResponse(ex.Message));
        }
    }

    [Function(nameof(DeleteDescendantTypeSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.DeleteDescendantTypeSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The descendant annotation type code.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The descendant-type schema doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "A concurrent modification prevented the deletion.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteDescendantTypeSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaDescendantType)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!TryValidateAnnotationKey(key, out var badRequestResult))
        {
            return badRequestResult;
        }

        if (!TryValidateAnnotationType(annotationType, out badRequestResult))
        {
            return badRequestResult;
        }

        try
        {
            var deleted = await _schemaService.DeleteDescendantTypeSchemaAsync(organization, project, view, key, annotationType);
            return deleted ? new OkResult() : new NotFoundResult();
        }
        catch (SchemaConcurrencyException ex)
        {
            return new ConflictObjectResult(new ErrorResponse(ex.Message));
        }
    }

    [Function(nameof(GetCombinedConfigurationSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetCombinedConfigurationSchema, Definitions.Tags.Schemas)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(CombinedConfigurationSchema), Description = "The combined configuration schema from all applicable levels.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "No schemas found for the specified annotation.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetCombinedConfigurationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.SchemaCombined)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryValidateAnnotationKey(key, out var badRequestResult))
        {
            return badRequestResult;
        }

        var combined = await _schemaService.GetCombinedSchemaAsync(organization, project, view, key);

        if (combined is null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(combined);
    }

    private bool TryValidateAnnotationType(string annotationType, out IActionResult badRequestResult)
    {
        return AnnotationTypeValidation.TryValidate(annotationType, _logger, out badRequestResult);
    }

    private bool TryValidateAnnotationKey(string annotationKey, out IActionResult badRequestResult)
    {
        if (AnnotationKey.TryParse(annotationKey, out _))
        {
            badRequestResult = null!;
            return true;
        }

        _logger.LogWarning("Invalid request; unknown annotation key '{annotationKey}'", annotationKey);
        badRequestResult = new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {annotationKey}"));
        return false;
    }

    private static ConflictObjectResult ToConflictResult(SchemaValidationException ex)
    {
        var errorDetails = ex.ValidationErrors
            .Select(e => new SchemaValidationErrorDetail
            {
                AnnotationKey = e.AnnotationKey,
                ViewName = e.ViewName,
                Errors = e.Errors,
            })
            .ToList();

        return new ConflictObjectResult(new SchemaValidationErrorResponse(ex.Message, errorDetails));
    }

    private static bool IsForce(HttpRequestData request)
    {
        return bool.TryParse(request.Query[Definitions.Parameters.Force], out var force) && force;
    }
}
