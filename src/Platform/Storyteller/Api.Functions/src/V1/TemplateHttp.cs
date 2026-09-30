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
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.V1;

public class TemplateHttp
{
    private readonly IConfigurationTemplateService _templates;
    private readonly IAccessService _access;
    private readonly ILogger<TemplateHttp> _logger;

    public TemplateHttp(
        IConfigurationTemplateService templates,
        IAccessService access,
        ILogger<TemplateHttp> logger)
    {
        _templates = templates;
        _access = access;
        _logger = logger;
    }

    [Function(nameof(GetTemplate))]
    [OpenApiOperation(Definitions.RouteIds.Template.GetTemplate, Definitions.Tags.Template)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationTemplate), Description = "The configuration template of the annotation type in the view.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "No template exists for the annotation type in the view.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetTemplate(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Template.V1.Template)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!AnnotationTypeValidation.TryValidate(annotationType, _logger, out var badRequestResult))
        {
            return badRequestResult;
        }

        var template = await _templates.GetTemplateAsync(organization, project, view, annotationType);

        if (template is null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(template);
    }

    [Function(nameof(SetTemplate))]
    [OpenApiOperation(Definitions.RouteIds.Template.SetTemplate, Definitions.Tags.Template)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(JObject), Description = "The template content, merged into the current template (supports $remove and $patch).")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationTemplate), Description = "The created or updated template.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetTemplate(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Definitions.Methods.Put, Route = Definitions.Routes.Template.V1.Template)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!AnnotationTypeValidation.TryValidate(annotationType, _logger, out var badRequestResult))
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
            _logger.LogError(e, "Error occurred while parsing input template.");
            return new BadRequestObjectResult(new ErrorResponse($"Invalid input template model: {e.Message}"));
        }

        try
        {
            var author = request.GetAuthor();
            var outputModel = await _templates.CreateOrUpdateTemplateAsync(organization, project, view, annotationType, inputModel, author);
            return new OkObjectResult(outputModel);
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new ErrorResponse(ex.Message));
        }
    }

    [Function(nameof(PatchTemplate))]
    [OpenApiOperation(Definitions.RouteIds.Template.PatchTemplate, Definitions.Tags.Template)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiRequestBody("application/json-patch+json", typeof(JArray), Description = "A JSON Patch document (RFC 6902) containing the operations to apply.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationTemplate), Description = "The patched template.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "No template exists for the annotation type in the view.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PatchTemplate(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Patch, Route = Definitions.Routes.Template.V1.Template)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!AnnotationTypeValidation.TryValidate(annotationType, _logger, out var badRequestResult))
        {
            return badRequestResult;
        }

        JArray patchOperations;

        try
        {
            using var sReader = new StreamReader(request.Body);
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
            var author = request.GetAuthor();
            var outputModel = await _templates.PatchTemplateAsync(organization, project, view, annotationType, patchOperations, author);
            return new OkObjectResult(outputModel);
        }
        catch (TemplateNotFoundException ex)
        {
            return new NotFoundObjectResult(new ErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new ErrorResponse(ex.Message));
        }
    }

    [Function(nameof(DeleteTemplate))]
    [OpenApiOperation(Definitions.RouteIds.Template.DeleteTemplate, Definitions.Tags.Template)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "No template exists for the annotation type in the view.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteTemplate(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Template.V1.Template)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        if (!AnnotationTypeValidation.TryValidate(annotationType, _logger, out var badRequestResult))
        {
            return badRequestResult;
        }

        var deleted = await _templates.DeleteTemplateAsync(organization, project, view, annotationType);
        return deleted ? new OkResult() : new NotFoundResult();
    }

    [Function(nameof(GetTemplateVersions))]
    [OpenApiOperation(Definitions.RouteIds.Template.GetTemplateVersions, Definitions.Tags.Template)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<ConfigurationVersion>), Description = "The list of template versions.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetTemplateVersions(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Template.V1.Versions)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!AnnotationTypeValidation.TryValidate(annotationType, _logger, out var badRequestResult))
        {
            return badRequestResult;
        }

        var versions = await _templates.GetTemplateVersionsAsync(organization, project, view, annotationType);
        return new OkObjectResult(versions);
    }

    [Function(nameof(GetTemplateVersion))]
    [OpenApiOperation(Definitions.RouteIds.Template.GetTemplateVersion, Definitions.Tags.Template)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ConfigurationTemplate), Description = "The template of the version.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested version doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetTemplateVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Template.V1.Version)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType,
        uint version)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!AnnotationTypeValidation.TryValidate(annotationType, _logger, out var badRequestResult))
        {
            return badRequestResult;
        }

        var template = await _templates.GetTemplateVersionContentAsync(organization, project, view, annotationType, version);

        if (template is null)
        {
            return new NotFoundResult();
        }

        return new OkObjectResult(template);
    }

    [Function(nameof(GetTemplateVersionDiff))]
    [OpenApiOperation(Definitions.RouteIds.Template.GetTemplateVersionDiff, Definitions.Tags.Template)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.AnnotationType, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.AnnotationType)]
    [OpenApiParameter(Definitions.Parameters.Version, In = ParameterLocation.Path, Required = true, Type = typeof(uint), Description = "The version number.")]
    [OpenApiParameter(Definitions.Parameters.Format, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.DiffFormat)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(DiffResult), Description = "The diff between this and previous version. When ?format=unified, returns text/plain instead.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The requested version doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetTemplateVersionDiff(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Template.V1.VersionDiff)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType,
        uint version)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!AnnotationTypeValidation.TryValidate(annotationType, _logger, out var badRequestResult))
        {
            return badRequestResult;
        }

        return await _templates
            .GetTemplateVersionChangesAsync(organization, project, view, annotationType, version)
            .ToDiffResponseAsync(request);
    }

    [Function(nameof(GetTemplateVersionDiffCustom))]
    [OpenApiOperation(Definitions.RouteIds.Template.GetTemplateVersionDiffCustom, Definitions.Tags.Template)]
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
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Configuration.Read}, {Scopes.Configuration.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetTemplateVersionDiffCustom(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Template.V1.VersionDiffCustom)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string annotationType,
        uint version,
        uint versionFrom)
    {
        request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!AnnotationTypeValidation.TryValidate(annotationType, _logger, out var badRequestResult))
        {
            return badRequestResult;
        }

        return await _templates
            .GetTemplateVersionChangesAsync(organization, project, view, annotationType, versionFrom, version)
            .ToDiffResponseAsync(request);
    }
}
