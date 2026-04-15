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

public class ConfigurationSchemaHttp
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
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.GetConfigurationSchema, Definitions.Tags.ConfigurationSchema)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The configuration schema for the specified annotation type.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested configuration schema doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.ConfigurationSchema.V1.Schema)]
        HttpRequestData request,
        string organization,
        string project,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryValidateAnnotationType(annotationType, out var badRequestResult))
        {
            return badRequestResult;
        }

        var schema = await _schemaService.GetSchemaAsync(organization, project, annotationType);

        if (schema is null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(schema);
    }

    [Function(nameof(SetConfigurationSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.SetConfigurationSchema, Definitions.Tags.ConfigurationSchema)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(JObject), Description = "The JSON Schema to apply to configurations of this annotation type.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationSchema), Description = "The created or updated configuration schema.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(SchemaValidationErrorResponse), Description = "Existing configurations are not compliant with the provided schema.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetConfigurationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.ConfigurationSchema.V1.Schema)]
        HttpRequestData request,
        string organization,
        string project,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

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
            var schema = await _schemaService.SetSchemaAsync(organization, project, annotationType, inputModel, author);
            return new OkObjectResult(schema);
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new ErrorResponse(ex.Message));
        }
        catch (SchemaValidationException ex)
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
    }

    [Function(nameof(DeleteConfigurationSchema))]
    [OpenApiOperation(Definitions.RouteIds.ConfigurationSchema.DeleteConfigurationSchema, Definitions.Tags.ConfigurationSchema)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The configuration schema doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteConfigurationSchema(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.ConfigurationSchema.V1.Schema)]
        HttpRequestData request,
        string organization,
        string project,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryValidateAnnotationType(annotationType, out var badRequestResult))
        {
            return badRequestResult;
        }

        var deleted = await _schemaService.DeleteSchemaAsync(organization, project, annotationType);

        if (!deleted)
        {
            return new NotFoundResult();
        }

        return new OkResult();
    }

    private bool TryValidateAnnotationType(string annotationType, out IActionResult badRequestResult)
    {
        if (AnnotationTypeCodes.ValidCodes.ContainsKey(annotationType))
        {
            badRequestResult = null!;
            return true;
        }

        _logger.LogWarning("Invalid request; unknown annotation type '{annotationType}'", annotationType);
        badRequestResult = new BadRequestObjectResult(new ErrorResponse($"Invalid annotation type: {annotationType}"));
        return false;
    }
}
