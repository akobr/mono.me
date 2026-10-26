using System.Diagnostics.CodeAnalysis;
using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.ConditionalReads;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.KeyValues.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.V1;

public partial class ConfigurationHttp
{
    // the d tags of one If-None-Match whose manifests a resolved read looks up
    private const int MaxManifestCandidates = 4;

    private readonly IConfigurationService _configuration;
    private readonly IAnnotationService _annotations;
    private readonly IAccessService _access;
    private readonly IKeyValuePolicyStore _policies;
    private readonly ILogger<ConfigurationHttp> _logger;

    public ConfigurationHttp(
        IConfigurationService configuration,
        IAnnotationService annotations,
        IAccessService access,
        IKeyValuePolicyStore policies,
        ILogger<ConfigurationHttp> logger)
    {
        _configuration = configuration;
        _annotations = annotations;
        _access = access;
        _policies = policies;
        _logger = logger;
    }

    [Function(nameof(GetConfigurations))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurations, Definitions.Tags.Configurations, Description = "Configurations of the view without their documents, up to 1000 per page.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only this annotation type code (rst, unt, sbt, usg, cnt, exe, uxe).")]
    [OpenApiParameter(Definitions.Parameters.KeyPrefix, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only annotation keys starting with this prefix.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationsResponse), Description = "One page of configuration summaries.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.Configurations)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? annotationType = null,
        [FromQuery] string? keyPrefix = null,
        [FromQuery] string? continuationToken = null)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.ConfigurationRead);

        if (!string.IsNullOrWhiteSpace(annotationType)
            && !AnnotationTypeValidation.TryValidate(annotationType.Trim(), _logger, out var badRequestResult))
        {
            return badRequestResult;
        }

        var response = await _configuration.ListConfigurationsAsync(organization, project, view, annotationType, keyPrefix, continuationToken);
        return new OkObjectResult(response);
    }

    [Function(nameof(GetConfiguration))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfiguration, Definitions.Tags.Configurations, Description = Definitions.Descriptions.ConditionalRead)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.IfNoneMatch, In = ParameterLocation.Header, Required = false, Type = typeof(string), Description = Definitions.Descriptions.IfNoneMatch)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Configuration), CustomHeaderType = typeof(ConfigurationReadResponseHeaders), Description = "The configuration model.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotModified, CustomHeaderType = typeof(ConfigurationReadResponseHeaders), Description = Definitions.Descriptions.ResponseNotModified)]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseConfigurationNotFound)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.Configuration)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Reader, MachineScope.ConfigurationRead);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var configurationModel = await _configuration.GetRawConfigurationAsync(fullKey);

        if (configurationModel is null)
        {
            return await NotFoundAsync(fullKey);
        }

        request.MarkNoCache();
        return request.OkOrNotModified(configurationModel, ConfigurationEntityTags.ForEffective(fullKey, configurationModel));
    }

    [Function(nameof(GetConfigurationResolved))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetResolvedConfiguration, Definitions.Tags.Configurations, Description = Definitions.Descriptions.ConditionalRead + " " + Definitions.Descriptions.ConditionalResolvedRead)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.IfNoneMatch, In = ParameterLocation.Header, Required = false, Type = typeof(string), Description = Definitions.Descriptions.IfNoneMatch)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Configuration), CustomHeaderType = typeof(ResolvedConfigurationReadResponseHeaders), Description = "The configuration model with resolved substitutions.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotModified, CustomHeaderType = typeof(ResolvedConfigurationReadResponseHeaders), Description = Definitions.Descriptions.ResponseNotModified)]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.UnprocessableEntity, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "A binding in the configuration cannot be evaluated or exceeds an evaluation limit. ErrorCode is binding.evaluation or binding.limit.<kind>.")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseConfigurationNotFound)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationResolved(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.ConfigurationResolved)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Reader, MachineScope.ConfigurationRead);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        // secrets go to machines only over mTLS when the project says so; a withheld read says why in a header
        var rights = (await request.GetSecretRightsAsync(_access, _policies, organization, project)).AsAmbient();
        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var configurationModel = await _configuration.GetRawConfigurationAsync(fullKey);

        if (configurationModel is null)
        {
            return await NotFoundAsync(fullKey);
        }

        // the effective inputs, tagged before anything is evaluated: without bindings the effective document is the
        // resolved one and is answered without evaluating; with bindings the tag is an input of the d tag
        var effectiveTag = ConfigurationEntityTags.ForResolved(fullKey, configurationModel, rights);
        var entityTag = effectiveTag;

        if (_configuration.HasBindings(configurationModel))
        {
            var manifests = request.GetDependencyManifests();
            var caller = DependencyManifests.CallerOf(rights);
            var runtime = manifests?.GetRuntimeVersion() ?? BindingRuntimeVersion.Of(catalog: null);
            var unchanged = await FindUnchangedManifestAsync(request, fullKey, effectiveTag, runtime, caller, manifests);

            if (unchanged is not null)
            {
                // nothing is evaluated, looked up, decrypted, metered or audited: the client holds the body already
                SetResolvedHeaders(request, rights);
                request.SetResponseHeader(HeaderNames.ETag, unchanged.ToString());
                return new StatusCodeResult(StatusCodes.Status304NotModified);
            }

            var dependencies = new BindingDependencies();
            configurationModel = await _configuration.ResolveConfigurationAsync(fullKey, configurationModel, rights.CanReadSecrets, dependencies);

            if (dependencies.IsVolatile)
            {
                // the body can change while the inputs do not (the clock, a binding that does not record), so only its own digest names it
                _logger.LogDebug("The resolved read of {Configuration} is tagged by its body: {Reason}", fullKey, dependencies.VolatileReason);
                entityTag = ConfigurationEntityTags.ForResolvedBindings(fullKey, configurationModel, rights, request.GetEntityTagKey());
            }
            else
            {
                var recorded = dependencies.ToList();
                entityTag = ConfigurationEntityTags.ForDependencies(effectiveTag, runtime, recorded);
                manifests?.Set(caller, new DependencyManifest(entityTag, effectiveTag, runtime, recorded));
            }
        }

        SetResolvedHeaders(request, rights);
        return request.OkOrNotModified(configurationModel, entityTag);
    }

    [Function(nameof(SetConfiguration))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.SetConfiguration, Definitions.Tags.Configurations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "When true, store the configuration even if it violates the schema.")]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(JObject), Description = "The configuration model.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Configuration), Description = "The created or updated configuration.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(SchemaValidationErrorResponse), Description = "The configuration violates the schema, the annotation is deleted (ErrorCode AnnotationDeleted), or the configuration was changed concurrently too many times (ErrorCode Conflict), and the request can be repeated.")]
    [OpenApiResponseWithBody(HttpStatusCode.PreconditionFailed, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponsePreconditionFailed)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationWrite)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Definitions.Methods.Put, Route = Definitions.Routes.Configuration.V1.Configuration)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Contributor, MachineScope.ConfigurationWrite);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        JObject inputModel;

        using var body = await request.ReadBodyAsync(RequestBodyLimits.Document);

        if (body is null)
        {
            return RequestBodyExtensions.BodyTooLarge(RequestBodyLimits.Document);
        }

        try
        {
            using var sReader = new StreamReader(body);
            await using var jReader = new JsonTextReader(sReader);
            inputModel = await JObject.LoadAsync(jReader);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error occurred while parsing input configuration.");
            return new BadRequestObjectResult(new ErrorResponse($"Invalid input configuration model: {e.Message}"));
        }

        try
        {
            var outputModel = await _configuration.CreateOrUpdateConfigurationAsync(fullKey, inputModel, request.GetAuditActor(), IsForce(request));
            return new OkObjectResult(outputModel);
        }
        catch (StorytellerException ex)
        {
            // a deleted annotation answers 409 AnnotationDeleted
            return ErrorResponseMapping.ToActionResult(ex);
        }
        catch (JsonPatchException ex)
        {
            // A failed test operation answers 412, any other patch failure 400.
            return ErrorResponseMapping.ToActionResult(ex);
        }
        catch (InvalidOperationException ex)
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

    [Function(nameof(PatchConfiguration))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.PatchConfiguration, Definitions.Tags.Configurations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Force, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "When true, store the configuration even if it violates the schema.")]
    [OpenApiRequestBody("application/json-patch+json", typeof(JArray), Description = "A JSON Patch document (RFC 6902) containing the operations to apply.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Configuration), Description = "The patched configuration.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The configuration doesn't exist or has no content to patch.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(SchemaValidationErrorResponse), Description = "The patch violates the configuration schema, the annotation is deleted (ErrorCode AnnotationDeleted), or the configuration was changed concurrently too many times (ErrorCode Conflict), and the request can be repeated.")]
    [OpenApiResponseWithBody(HttpStatusCode.PreconditionFailed, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponsePreconditionFailed)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationWrite)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PatchConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Patch, Route = Definitions.Routes.Configuration.V1.Configuration)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Contributor, MachineScope.ConfigurationWrite);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        JArray patchOperations;

        using var body = await request.ReadBodyAsync(RequestBodyLimits.Document);

        if (body is null)
        {
            return RequestBodyExtensions.BodyTooLarge(RequestBodyLimits.Document);
        }

        try
        {
            using var sReader = new StreamReader(body);
            await using var jReader = new JsonTextReader(sReader);
            patchOperations = await JArray.LoadAsync(jReader);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error occurred while parsing JSON Patch document.");
            return new BadRequestObjectResult(new ErrorResponse($"Invalid JSON Patch document: {e.Message}"));
        }

        try
        {
            var outputModel = await _configuration.PatchConfigurationAsync(fullKey, patchOperations, request.GetAuditActor(), IsForce(request));
            return new OkObjectResult(outputModel);
        }
        catch (StorytellerException ex)
        {
            // a deleted annotation answers 409 AnnotationDeleted
            return ErrorResponseMapping.ToActionResult(ex);
        }
        catch (ConfigurationNotFoundException ex)
        {
            return new NotFoundObjectResult(new ErrorResponse(ex.Message));
        }
        catch (JsonPatchException ex)
        {
            // A failed test operation answers 412, any other patch failure 400.
            return ErrorResponseMapping.ToActionResult(ex);
        }
        catch (InvalidOperationException ex)
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

    [Function(nameof(DeleteConfiguration))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.DeleteConfiguration, Definitions.Tags.Configurations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The configuration doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The annotation is deleted (ErrorCode AnnotationDeleted), or the configuration was changed concurrently too many times (ErrorCode Conflict), and the request can be repeated.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationWrite)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Configuration.V1.Configuration)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Contributor, MachineScope.ConfigurationWrite);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);

        try
        {
            // a clear that finds nothing still finishes the cache invalidation an earlier clear left pending
            return await _configuration.ClearConfigurationAsync(fullKey, request.GetAuditActor())
                ? new OkResult()
                : new NotFoundResult();
        }
        catch (StorytellerException ex)
        {
            // a deleted annotation answers 409 AnnotationDeleted, a write that kept losing to concurrent ones 409 Conflict
            return ErrorResponseMapping.ToActionResult(ex);
        }
    }

    [Function(nameof(GetConfigurationVersions))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurationVersions, Definitions.Tags.Configurations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<ConfigurationVersion>), Description = "The list of configuration versions.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationVersions(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.Versions)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Reader, MachineScope.ConfigurationRead);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var versions = await _configuration.GetConfigurationVersionsAsync(fullKey);

        // the versions of a deleted annotation are hidden until a restore
        return versions.Count == 0
            ? await NotFoundAsync(fullKey, new OkObjectResult(versions))
            : new OkObjectResult(versions);
    }

    [Function(nameof(GetConfigurationVersion))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurationVersion, Definitions.Tags.Configurations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Configuration), Description = "The configuration of the version.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The requested version doesn't exist, or the annotation is deleted (ErrorCode AnnotationDeleted).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.Version)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Reader, MachineScope.ConfigurationRead);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var configurationModel = await _configuration.GetConfigurationVersionContentAsync(fullKey, version);

        if (configurationModel is null)
        {
            return await NotFoundAsync(fullKey);
        }

        return new OkObjectResult(configurationModel);
    }

    [Function(nameof(GetConfigurationVersionDiff))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurationVersionDiff, Definitions.Tags.Configurations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between this and previous version. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The requested version doesn't exist, or the annotation is deleted (ErrorCode AnnotationDeleted).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationVersionDiff(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.VersionDiff)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Reader, MachineScope.ConfigurationRead);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var diff = await _configuration
            .GetConfigurationVersionChangesAsync(fullKey, version)
            .ToDiffResponseAsync(request);
        return diff is NotFoundObjectResult ? await NotFoundAsync(fullKey, diff) : diff;
    }

    [Function(nameof(GetConfigurationVersionDiffCustom))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurationVersionDiffCustom, Definitions.Tags.Configurations)]
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
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "One of the requested versions doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationVersionDiffCustom(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.VersionDiffCustom)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        uint version,
        uint versionFrom)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Reader, MachineScope.ConfigurationRead);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var diff = await _configuration
            .GetConfigurationVersionChangesAsync(fullKey, versionFrom, version)
            .ToDiffResponseAsync(request);
        return diff is NotFoundObjectResult ? await NotFoundAsync(fullKey, diff) : diff;
    }

    [Function(nameof(GetConfigurationViewDiff))]
    [OpenApiOperation(Definitions.RouteIds.Configuration.GetConfigurationViewDiff, Definitions.Tags.Configurations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.ViewTo, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The target view name.")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between the two views. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "One of the requested configurations doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenConfigurationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationViewDiff(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.ViewDiff)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string viewTo)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Reader, MachineScope.ConfigurationRead);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var diff = await _configuration
            .GetConfigurationViewChangesAsync(fullKey, viewTo)
            .ToDiffResponseAsync(request);
        return diff is NotFoundObjectResult ? await NotFoundAsync(fullKey, diff) : diff;
    }

    private bool TryParseAnnotationKey(
        string annotationKey,
        [MaybeNullWhen(false)] out AnnotationKey key,
        [MaybeNullWhen(true)] out IActionResult badRequestResult)
    {
        if (AnnotationKey.TryParse(annotationKey, out key))
        {
            badRequestResult = null;
            return true;
        }

        _logger.LogWarning("Invalid request; unknown annotation key '{annotationKey}'", annotationKey);
        badRequestResult = new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {annotationKey}"));
        return false;
    }

    // A read that found nothing: a deleted annotation answers 404 with ErrorCode AnnotationDeleted, so clients can tell
    // it from a missing one; otherwise the fallback (a plain 404). Costs one point read, only on this path.
    private async Task<IActionResult> NotFoundAsync(FullKey key, IActionResult? fallback = null)
    {
        if (await _annotations.GetDeletedAnnotationAsync(key) is null)
        {
            return fallback ?? new NotFoundResult();
        }

        return new NotFoundObjectResult(new ErrorResponse($"The annotation '{key.Annotation}' is deleted; restore it to see its configuration.")
        {
            ErrorCode = ErrorCodes.AnnotationDeleted,
        });
    }

    // The d tag of If-None-Match whose manifest this caller recorded under the same effective document and runtime, when
    // none of its inputs changed; null otherwise. The candidates are compared by their recorded inputs, in constant time,
    // so a long header costs no hashing; at most MaxManifestCandidates distinct d tags are looked up and revalidated, in order,
    // so an older tag listed first does not hide a newer one that is still current.
    private async Task<EntityTagHeaderValue?> FindUnchangedManifestAsync(
        HttpRequestData request,
        FullKey key,
        EntityTagHeaderValue effectiveTag,
        string runtime,
        string caller,
        DependencyManifests? manifests)
    {
        if (manifests is null)
        {
            return null;
        }

        var candidates = request.GetIfNoneMatch()
            .Where(ConfigurationEntityTags.IsDependencies)
            .DistinctBy(tag => tag.Tag.ToString())
            .Take(MaxManifestCandidates);

        foreach (var tag in candidates)
        {
            if (!manifests.TryGet(caller, tag, out var manifest)
                || !manifest.Effective.Equals(effectiveTag)
                || !string.Equals(manifest.Runtime, runtime, StringComparison.Ordinal))
            {
                continue;
            }

            if (await _configuration.AreDependenciesUnchangedAsync(key, manifest.Dependencies))
            {
                return manifest.Tag;
            }

            manifests.Remove(caller, manifest.Tag);
        }

        return null;
    }

    // The same on the 200 and the 304s. Only the read right counts: mTLS would not let a machine with Secrets.Write alone
    // read secrets.
    private static void SetResolvedHeaders(HttpRequestData request, SecretRights rights)
    {
        if (rights.IsReadWithheldByMtls)
        {
            request.SetResponseHeader(SecretRights.WithheldHeader, SecretRights.WithheldByMtls);
        }

        if (rights.CanReadSecrets)
        {
            request.MarkNoStore();
        }
        else
        {
            request.MarkNoCache();
        }
    }

    private static bool IsForce(HttpRequestData request)
    {
        return bool.TryParse(request.Query[Definitions.Parameters.Force], out var force) && force;
    }
}
