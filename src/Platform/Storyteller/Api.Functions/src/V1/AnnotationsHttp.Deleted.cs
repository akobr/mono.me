using System.Net;
using _42.Platform.Storyteller.Accessing;
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

// Deleted annotations (listing, lookup, restore, permanent delete) and the history of an annotation.
public partial class AnnotationsHttp
{
    [Function(nameof(GetDeletedAnnotations))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetDeletedAnnotations, Definitions.Tags.Annotations, Description = "The deleted annotations of the view, newest deletion first, each with its Deletion (who, when, the root, and when it is purged automatically).")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Roots, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "Only the roots of deletions (default true); false lists every deleted annotation.")]
    [OpenApiParameter(Definitions.Parameters.DeletionId, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Only what one delete removed.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse), Description = "One page of deleted annotations.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetDeletedAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Deleted)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] bool roots = true,
        [FromQuery] string? deletionId = null,
        [FromQuery] string? continuationToken = null)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var response = await _annotations.GetDeletedAnnotationsAsync(organization, project, view, roots, deletionId, continuationToken);
        return new OkObjectResult(response);
    }

    [Function(nameof(GetDeletedAnnotation))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetDeletedAnnotation, Definitions.Tags.Annotations, Description = "A deleted annotation with its Deletion.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Annotation), Description = "The deleted annotation.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key is not valid (InvalidKey).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The annotation is not deleted, or does not exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetDeletedAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.DeletedAnnotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key);

        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return InvalidKey(key);
        }

        var annotation = await _annotations.GetDeletedAnnotationAsync(FullKey.Create(annotationKey, organization, project, view));
        return annotation is null ? new NotFoundResult() : new OkObjectResult(annotation);
    }

    [Function(nameof(FindDeletedAnnotations))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.FindDeletedAnnotations, Definitions.Tags.Annotations, Description = "The deleted annotations among the keys. Clients call it with the keys they are about to create and their ancestors, because a create re-creates a deleted key or ancestor.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(AnnotationKeysRequest), Description = "Up to 100 annotation keys.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(IEnumerable<Annotation>), Description = "The deleted annotations among the keys; active and unknown keys are left out.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "No keys, more than 100 keys, or an invalid key (InvalidKey).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> FindDeletedAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Annotations.V1.DeletedLookup)]
        HttpRequestData request,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        var (model, invalidBody) = await request.ReadJsonBodyAsync<AnnotationKeysRequest>(_bodyJsonOptions, "an annotation lookup request", ErrorCodes.InvalidKey);

        // The keys of the body are authorized before any 400 of the body; a body that cannot be read names no key.
        await request.CheckAccessToAnnotationsAsync(_access, organization, project, model?.Keys ?? []);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (!TryParseKeys(model?.Keys, out var keys, out var badRequest))
        {
            return badRequest;
        }

        var annotations = await _annotations.FindDeletedAnnotationsAsync(organization, project, view, keys);
        return new OkObjectResult(annotations);
    }

    [Function(nameof(RestoreAnnotations))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.RestoreAnnotations, Definitions.Tags.Annotations, Description = "Restores deleted annotations, ancestors first. Deleted ancestors are restored alone and missing ones created; with Descendants (default), also everything each key's deletion removed below it. A key that fails does not stop the others.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(AnnotationRestoreRequest), Description = "Up to 100 annotation keys, and whether their descendants are restored too.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationRestoreResult), Description = "The restored and created annotations, ancestors first, and the keys that failed.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "No keys, more than 100 keys, or an invalid key (InvalidKey).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> RestoreAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Annotations.V1.DeletedRestore)]
        HttpRequestData request,
        string organization,
        string project,
        string view)
    {
        request.CheckScope(Scopes.Annotation.Write, Scopes.Default.Write);
        var (model, invalidBody) = await request.ReadJsonBodyAsync<AnnotationRestoreRequest>(_bodyJsonOptions, "an annotation restore request", ErrorCodes.InvalidKey);

        // The keys of the body are authorized before any 400 of the body; a body that cannot be read names no key.
        var restriction = await request.CheckAccessToAnnotationsAsync(_access, organization, project, model?.Keys ?? [], AccountRole.Contributor);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (!TryParseKeys(model?.Keys, out var keys, out var badRequest))
        {
            return badRequest;
        }

        var result = await _annotations.RestoreAnnotationsAsync(organization, project, view, keys, request.GetAuditActor(), model!.Descendants);

        // The deleted ancestors come back too; a machine restricted to an annotation sees only what it may address.
        if (restriction.IsRestricted)
        {
            result = result with
            {
                Annotations = result.Annotations.Where(annotation => restriction.Allows(annotation.AnnotationKey)).ToList(),
                CreatedKeys = result.CreatedKeys.Where(restriction.Allows).ToList(),
            };
        }

        return new OkObjectResult(result);
    }

    [Function(nameof(PurgeAnnotation))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.PurgeAnnotation, Definitions.Tags.Annotations, Description = "Deletes a deleted annotation permanently, with its descendants, their configurations (the content stays in the versions) and their history. Needs a user with the Administrator role.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationPurgeResult), Description = "The purged keys, descendants first and the annotation last.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key is not valid (InvalidKey).")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The annotation does not exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The annotation is not deleted (AnnotationNotDeleted), or a descendant was restored meanwhile (Conflict).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PurgeAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Annotations.V1.DeletedAnnotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Annotation.Write, Scopes.Default.Write);

        // machine access is checked per project without roles, so a permanent delete is for users only
        if (request.TryGetApplicationIdentity(out _))
        {
            throw new AccessDeniedException("A permanent delete needs a user with the Administrator role.");
        }

        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return InvalidKey(key);
        }

        var result = await _annotations.PurgeAnnotationAsync(FullKey.Create(annotationKey, organization, project, view), request.GetAuditActor());
        return new OkObjectResult(result);
    }

    [Function(nameof(GetAnnotationHistory))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetAnnotationHistory, Definitions.Tags.Annotations, Description = "What happened with an active or deleted annotation and who did it, newest first. Updates carry the previous and the new values; the description is only flagged.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationHistory), Description = "The history of the annotation.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The key is not valid (InvalidKey).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The annotation does not exist or was deleted permanently.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAnnotationHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.History)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key);

        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return InvalidKey(key);
        }

        var history = await _annotations.GetAnnotationHistoryAsync(FullKey.Create(annotationKey, organization, project, view));
        return history is null ? new NotFoundResult() : new OkObjectResult(history);
    }

    private static IActionResult InvalidKey(string key)
    {
        return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {key}") { ErrorCode = ErrorCodes.InvalidKey });
    }

    // The keys of a lookup or a restore: at least one, at most ApiConstants.MaxInputItems, all valid.
    private static bool TryParseKeys(
        IReadOnlyList<string>? input,
        out IReadOnlyList<AnnotationKey> keys,
        out IActionResult badRequest)
    {
        keys = [];
        badRequest = null!;

        if (input is null || input.Count == 0)
        {
            badRequest = new BadRequestObjectResult(new ErrorResponse("No annotation keys in the body.") { ErrorCode = ErrorCodes.InvalidKey });
            return false;
        }

        if (input.Count > ApiConstants.MaxInputItems)
        {
            badRequest = new BadRequestObjectResult(new ErrorResponse($"Too many annotation keys, at most {ApiConstants.MaxInputItems} are allowed.") { ErrorCode = ErrorCodes.InvalidKey });
            return false;
        }

        var parsed = new List<AnnotationKey>();
        var invalid = new List<string>();

        foreach (var text in input)
        {
            if (AnnotationKey.TryParse(text, out var key))
            {
                parsed.Add(key);
            }
            else
            {
                invalid.Add(text);
            }
        }

        if (invalid.Count > 0)
        {
            badRequest = new BadRequestObjectResult(new ErrorResponse($"Invalid annotation keys: {string.Join(", ", invalid)}") { ErrorCode = ErrorCodes.InvalidKey });
            return false;
        }

        keys = parsed;
        return true;
    }
}
