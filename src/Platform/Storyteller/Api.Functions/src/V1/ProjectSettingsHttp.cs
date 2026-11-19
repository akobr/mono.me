using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.ConditionalReads;
using _42.Platform.Storyteller.Api.ConditionalWrites;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.KeyValues;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

// The settings of a project: how every client shows its annotation types (names, colors, icons), the time zone new
// annotations start in and the date format. Every member of the project reads them, also a machine restricted to an
// annotation (they hold no annotation data); only users with the Administrator role change them. The ETag is the
// settings' Version in quotes, so a write can be conditional on the read it comes from.
public class ProjectSettingsHttp
{
    // Machines have no role, so the Administrator writes are for users only.
    public const string ChangingSettingsNeedsUser = "Changing the project settings needs a user with the Administrator role.";

    // The patch format of PatchProjectSettings (RFC 5789 §3.1), answered with a 415.
    private const string AcceptPatchHeader = "Accept-Patch";

    // a member twice in a body is ambiguous, so it is refused
    private static readonly JsonDocumentOptions BodyDocumentOptions = new() { AllowDuplicateProperties = false };

    private readonly IProjectSettingsService _settings;
    private readonly IAccessService _access;

    public ProjectSettingsHttp(IProjectSettingsService settings, IAccessService access)
    {
        _settings = settings;
        _access = access;
    }

    [Function(nameof(GetProjectSettings))]
    [OpenApiOperation(Definitions.RouteIds.Access.GetProjectSettings, Definitions.Tags.Access, Description = "The settings of the project: the names, colors and icons of its annotation types, the time zone new annotations start in and the date format, for every client to show the project the same way. When nothing was set: Types {}, no DefaultTimeZone (UTC), no DateFormat (yyyy-mm-dd) and Version 0. Every member of the project reads them, also a machine restricted to an annotation; the ETag is the Version in quotes.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.IfNoneMatch, In = ParameterLocation.Header, Required = false, Type = typeof(string), Description = Definitions.Descriptions.IfNoneMatchProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ProjectSettings), CustomHeaderType = typeof(ProjectSettingsReadResponseHeaders), Description = "The settings of the project, with the ETag of their version.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotModified, CustomHeaderType = typeof(ProjectSettingsReadResponseHeaders), Description = Definitions.Descriptions.ResponseNotModifiedProjectSettings)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotFoundProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetProjectSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Access.V1.ProjectSettings)]
        HttpRequestData request,
        string organization,
        string project)
    {
        // The settings hold no annotation data, so a machine restricted to an annotation reads them too, and either
        // read scope is enough, as for the views.
        await request.CheckAccessToProjectMetadataAsync(_access, organization, project, AccountRole.Reader, MachineScope.AnnotationRead, MachineScope.ConfigurationRead);

        var settings = await _settings.GetAsync(organization, project, request.FunctionContext.CancellationToken);
        request.MarkNoCache();
        return request.OkOrNotModified(settings, ProjectSettingsConditions.EntityTagOf(settings.Version));
    }

    [Function(nameof(SetProjectSettings))]
    [OpenApiOperation(Definitions.RouteIds.Access.SetProjectSettings, Definitions.Tags.Access, Description = "Replaces the settings of the project: a type left out goes back to its built-in appearance, a missing DefaultTimeZone or DateFormat to its default. Version and Modified in the body are ignored, and Format is optional: when it is given, it must be 2s.project-settings.v1. So a read or a data set's project-settings.json can be sent back as it is. Every value is checked against the catalog of project settings; a write that changes nothing keeps the version. Needs a user with the Administrator role.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.IfMatch, In = ParameterLocation.Header, Required = false, Type = typeof(string), Description = Definitions.Descriptions.IfMatchProjectSettings)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(ProjectSettings), Required = true, Description = "The new settings of the project.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ProjectSettings), CustomHeaderType = typeof(ProjectSettingsResponseHeaders), Description = "The stored settings, with the ETag of their version; also for a write that changed nothing.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequestProjectSettings)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenProjectSettingsWrite)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotFoundProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseConflictProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.PreconditionFailed, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponsePreconditionFailedProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.RequestEntityTooLarge, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBodyTooLargeProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.UnsupportedMediaType, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The body is application/merge-patch+json or application/json-patch+json (ErrorCode InvalidContentType): a PUT replaces the whole settings, so a partial body would reset every type it leaves out. Send a merge patch with PATCH. A Content-Type with several media types, or one that is no media type, is refused the same way.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetProjectSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Put, Route = Definitions.Routes.Access.V1.ProjectSettings)]
        HttpRequestData request,
        string organization,
        string project)
    {
        request.EnsureUser(ChangingSettingsNeedsUser);
        await request.CheckUserAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        if (!ProjectSettingsConditions.TryRead(request, out var condition, out var conditionError))
        {
            return conditionError!;
        }

        if (!TryReadMediaType(request, out var mediaType))
        {
            return new ObjectResult(new ErrorResponse($"The Content-Type is not one media type; a PUT of the project settings is {Definitions.ContentTypes.Json}.")
            {
                ErrorCode = ErrorCodes.InvalidContentType,
            })
            {
                StatusCode = (int)HttpStatusCode.UnsupportedMediaType,
            };
        }

        // A patch sent to the PUT would replace the settings with its few members and reset every type it leaves out.
        if (IsPatch(mediaType))
        {
            return new ObjectResult(new ErrorResponse($"A PUT replaces the whole project settings with an {Definitions.ContentTypes.Json} body; send a merge patch ({Definitions.ContentTypes.MergePatch}) with PATCH.")
            {
                ErrorCode = ErrorCodes.InvalidContentType,
            })
            {
                StatusCode = (int)HttpStatusCode.UnsupportedMediaType,
            };
        }

        var (body, bodyError) = await ReadBodyAsync(request);

        if (bodyError is not null)
        {
            return bodyError;
        }

        JsonNode? document;

        try
        {
            document = body.Length == 0 ? null : EnsureText(JsonNode.Parse(body.Span, documentOptions: BodyDocumentOptions));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            // ArgumentException or InvalidOperationException (EnsureText): an escape that is no UTF-16 text, such as a lone
            // surrogate
            return ErrorResponseMapping.ToActionResult(new ProjectSettingsInvalidException(
                [new ProjectSettingsProblem("$", ProjectSettingsRules.WrongType, $"The body is not JSON: {exception.Message}")]));
        }

        var check = ProjectSettingsJson.Read(document);

        if (!check.IsValid)
        {
            return ErrorResponseMapping.ToActionResult(new ProjectSettingsInvalidException(check.Problems));
        }

        return await WriteAsync(request, () => _settings.SetAsync(organization, project, check.Settings, condition, CallerOf(request), request.FunctionContext.CancellationToken));
    }

    [Function(nameof(PatchProjectSettings))]
    [OpenApiOperation(Definitions.RouteIds.Access.PatchProjectSettings, Definitions.Tags.Access, Description = "Changes members of the settings of the project, merged on the server against their current state. The body is a JSON Merge Patch (RFC 7396, application/merge-patch+json; application/json is taken too): a member left out keeps its value, null removes it (a type goes back to its built-in appearance, DefaultTimeZone to UTC, DateFormat to yyyy-mm-dd), Types merge type by type and the members of a type one by one; member names and type codes match case-insensitively. The result is checked as a full write; a patch that changes nothing keeps the version. Needs a user with the Administrator role.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.IfMatch, In = ParameterLocation.Header, Required = false, Type = typeof(string), Description = Definitions.Descriptions.IfMatchProjectSettings)]
    [OpenApiRequestBody(Definitions.ContentTypes.MergePatch, typeof(ProjectSettingsMergePatch), Required = true, Description = "A JSON Merge Patch of the settings (application/merge-patch+json).")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(ProjectSettings), CustomHeaderType = typeof(ProjectSettingsResponseHeaders), Description = "The stored settings, with the ETag of their version; also for a patch that changed nothing.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequestProjectSettingsPatch)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorizedUserOnly)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenProjectSettingsWrite)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseNotFoundProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseConflictProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.PreconditionFailed, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponsePreconditionFailedProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.RequestEntityTooLarge, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBodyTooLargeProjectSettings)]
    [OpenApiResponseWithBody(HttpStatusCode.UnsupportedMediaType, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The body is neither application/merge-patch+json nor application/json (ErrorCode InvalidContentType); Accept-Patch names the patch format.")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> PatchProjectSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Patch, Route = Definitions.Routes.Access.V1.ProjectSettings)]
        HttpRequestData request,
        string organization,
        string project)
    {
        request.EnsureUser(ChangingSettingsNeedsUser);
        await request.CheckUserAccessToProjectAsync(_access, organization, project, AccountRole.Administrator);

        if (!ProjectSettingsConditions.TryRead(request, out var condition, out var conditionError))
        {
            return conditionError!;
        }

        if (!IsMergePatch(request))
        {
            request.SetResponseHeader(AcceptPatchHeader, Definitions.ContentTypes.MergePatch);
            return new ObjectResult(new ErrorResponse($"A patch of the project settings is {Definitions.ContentTypes.MergePatch}.")
            {
                ErrorCode = ErrorCodes.InvalidContentType,
            })
            {
                StatusCode = (int)HttpStatusCode.UnsupportedMediaType,
            };
        }

        var (body, bodyError) = await ReadBodyAsync(request);

        if (bodyError is not null)
        {
            return bodyError;
        }

        JsonObject patch;

        try
        {
            patch = EnsureText(JsonNode.Parse(body.Span, documentOptions: BodyDocumentOptions)) as JsonObject
                ?? throw new InvalidOperationException("A merge patch (RFC 7396) of the project settings is a JSON object.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return new BadRequestObjectResult(new ErrorResponse($"The body is not a merge patch of the project settings: {exception.Message}")
            {
                ErrorCode = ErrorCodes.PatchInvalid,
            });
        }

        return await WriteAsync(request, () => _settings.PatchAsync(organization, project, patch, condition, CallerOf(request), request.FunctionContext.CancellationToken));
    }

    // A user's write: the User channel of the project's audit trail, the invocation as the correlation id.
    private static KeyValueCaller CallerOf(HttpRequestData request)
    {
        return KeyValueCaller.WithoutSecrets(request.GetAuditActor()) with { CorrelationId = request.FunctionContext.InvocationId };
    }

    // The node, after every name and string of it was decoded once: a parse of bytes lets an escape through that is no
    // UTF-16 text (a lone surrogate), and its decoding throws InvalidOperationException.
    private static JsonNode? EnsureText(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var (_, value) in members)
                {
                    EnsureText(value);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    EnsureText(item);
                }

                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                value.GetValue<string>();
                break;
        }

        return node;
    }

    // application/merge-patch+json, application/json, or no media type at all.
    private static bool IsMergePatch(HttpRequestData request)
    {
        return TryReadMediaType(request, out var mediaType)
            && (mediaType is null
                || mediaType.Equals(Definitions.ContentTypes.MergePatch, StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals(Definitions.ContentTypes.Json, StringComparison.OrdinalIgnoreCase));
    }

    // application/merge-patch+json or application/json-patch+json: a body that changes settings rather than holds them.
    private static bool IsPatch(string? mediaType)
    {
        return mediaType is not null
            && (mediaType.Equals(Definitions.ContentTypes.MergePatch, StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals(Definitions.ContentTypes.JsonPatch, StringComparison.OrdinalIgnoreCase));
    }

    // The media type of the body, null without a Content-Type; false for several values, or one that is no media type.
    private static bool TryReadMediaType(HttpRequestData request, out string? mediaType)
    {
        mediaType = null;

        if (!request.Headers.TryGetValues(HeaderNames.ContentType, out var values))
        {
            return true;
        }

        var given = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();

        if (given.Count == 0)
        {
            return true;
        }

        if (given.Count > 1 || !MediaTypeHeaderValue.TryParse(given[0], out var parsed))
        {
            return false;
        }

        mediaType = parsed.MediaType.Value;
        return true;
    }

    // The body without its byte order mark, or the 413 of a body above RequestBodyLimits.ProjectSettings. A body of
    // only white space is no body.
    private static async Task<(ReadOnlyMemory<byte> Body, IActionResult? Error)> ReadBodyAsync(HttpRequestData request)
    {
        using var body = new MemoryStream();

        if (!await request.TryCopyBodyAsync(body, RequestBodyLimits.ProjectSettings, request.FunctionContext.CancellationToken))
        {
            return (ReadOnlyMemory<byte>.Empty, RequestBodyExtensions.BodyTooLarge(RequestBodyLimits.ProjectSettings));
        }

        var bytes = body.WithoutByteOrderMark().ToArray();
        return (bytes.AsSpan().IndexOfAnyExcept(" \t\r\n"u8) < 0 ? ReadOnlyMemory<byte>.Empty : bytes, null);
    }

    // The write, its ETag on the 200, and the answers of invalid settings (400 with every problem) and of a failed
    // condition (412 with the current version); a missing project (404) and a conflict (409) go to the middleware.
    private static async Task<IActionResult> WriteAsync(HttpRequestData request, Func<Task<ProjectSettingsWrite>> write)
    {
        try
        {
            var written = await write();
            request.SetResponseHeader(HeaderNames.ETag, ProjectSettingsConditions.EntityTagOf(written.Settings.Version).ToString());
            return new OkObjectResult(written.Settings);
        }
        catch (Exception exception) when (exception is ProjectSettingsInvalidException or ProjectSettingsPreconditionFailedException)
        {
            return ErrorResponseMapping.ToActionResult(exception);
        }
    }
}
