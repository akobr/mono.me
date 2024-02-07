using System.Diagnostics.CodeAnalysis;
using System.Net;
using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
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

public class ConfigurationHttp
{
    private readonly IConfigurationService _configuration;
    private readonly IAccessService _access;
    private readonly ILogger<ConfigurationHttp> _logger;

    public ConfigurationHttp(
        IConfigurationService configuration,
        IAccessService access,
        ILogger<ConfigurationHttp> logger)
    {
        _configuration = configuration;
        _access = access;
        _logger = logger;
    }

    [Function(nameof(GetConfiguration))]
    [OpenApiOperation(Definitions.Routes.Configuration.V1.Configuration, Definitions.Tags.Configuration)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(JObject), Description = "The configuration model.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested configuration doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.Configuration)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var configurationModel = await _configuration.GetConfigurationAsync(fullKey);

        if (configurationModel is null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(configurationModel);
    }

    [Function(nameof(GetConfigurationResolved))]
    [OpenApiOperation(Definitions.Routes.Configuration.V1.ConfigurationResolved, Definitions.Tags.Configuration)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(JObject), Description = "The configuration model with resolved substitutions.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested configuration doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetConfigurationResolved(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Configuration.V1.ConfigurationResolved)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var configurationModel = await _configuration.GetConfigurationAsync(fullKey);

        if (configurationModel is null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(configurationModel);
    }

    [Function(nameof(SetConfiguration))]
    [OpenApiOperation(Definitions.Routes.Configuration.V1.Configuration, Definitions.Tags.Configuration)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(JObject), Description = "The configuration model.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(JObject), Description = "The created or updated configuration.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Definitions.Methods.Put, Route = Definitions.Routes.Configuration.V1.Configuration)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        JObject inputModel;

        try
        {
            using var sReader = new StreamReader(request.Body);
            await using var jReader = new JsonTextReader(sReader);
            inputModel = await JObject.LoadAsync(jReader);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error occurred while parsing input configuration.");
            return new BadRequestObjectResult(new ErrorResponse($"Invalid input configuration model: {e.Message}"));
        }

        var outputModel = await _configuration.CreateOrUpdateConfigurationAsync(fullKey, inputModel);
        return new OkObjectResult(outputModel);
    }

    [Function(nameof(DeleteConfiguration))]
    [OpenApiOperation(Definitions.Routes.Configuration.V1.Configuration, Definitions.Tags.Configuration)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The configuration doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Configuration.V1.Configuration)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!TryParseAnnotationKey(key, out var annotationKey, out var badRequestResult))
        {
            return badRequestResult;
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        return await _configuration.DeleteConfigurationAsync(fullKey)
            ? new OkResult()
            : new NotFoundResult();
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
}
