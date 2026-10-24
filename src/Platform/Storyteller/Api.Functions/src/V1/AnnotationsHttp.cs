using System.Linq.Expressions;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

public partial class AnnotationsHttp
{
    private static readonly JsonSerializerOptions AnnotationBodyOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAnnotationService _annotations;
    private readonly IAccessService _access;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _bodyJsonOptions;
    private readonly ILogger<AnnotationsHttp> _logger;

    public AnnotationsHttp(
        IAnnotationService annotations,
        IAccessService access,
        TimeProvider timeProvider,
        IOptions<JsonOptions> jsonOptions,
        ILogger<AnnotationsHttp> logger)
    {
        _annotations = annotations;
        _access = access;
        _timeProvider = timeProvider;
        _bodyJsonOptions = jsonOptions.Value.JsonSerializerOptions;
        _logger = logger;
    }

    [Function(nameof(GetAnnotations))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetAnnotations, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse), Description = "The list of annotations.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Annotations)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? continuationToken = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.AnnotationRead);

        var dataRequest = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Responsibility,
                AnnotationType.Subject,
                AnnotationType.Usage,
                AnnotationType.Context,
                AnnotationType.Execution,
                AnnotationType.Unit,
                AnnotationType.UnitOfExecution,
            },
            ContinuationToken = continuationToken,
            IncludeInactive = includeInactive,
            Deleted = DeletedFilter(includeDeleted),
            Organization = organization,
            Project = project,
            View = view,
        };

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        var result = new OkObjectResult(response);
        return result;
    }

    [Function(nameof(GetAnnotation))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetAnnotation, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Annotation), Description = "The requested annotation.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = "The requested annotation doesn't exist or is deleted, or it is not active and includeInactive is not set (ErrorCode AnnotationInactive).")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Annotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Reader, MachineScope.AnnotationRead);

        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {key}"));
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var annotation = await _annotations.GetAnnotationAsync(fullKey);

        if (annotation is null && includeDeleted)
        {
            annotation = await _annotations.GetDeletedAnnotationAsync(fullKey);
        }

        if (annotation is null)
        {
            return new NotFoundResult();
        }

        // the same rule as the lists: an inactive annotation is read only on request, and the error code tells it
        // from a missing one
        var status = annotation.GetStatus(_timeProvider.GetUtcNow());

        if (!includeInactive && status != AnnotationStatus.Active)
        {
            return new NotFoundObjectResult(new ErrorResponse($"The annotation '{key}' is {status.ToString().ToLowerInvariant()}; read it with includeInactive=true.")
            {
                ErrorCode = ErrorCodes.AnnotationInactive,
            });
        }

        return new OkObjectResult(annotation);
    }

    [Function(nameof(GetDescendants))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetDescendants, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiParameter(Definitions.Parameters.Descendants, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The type of descendants to retrieve, possible value is: usages, contexts, executions, units, units-of-execution, or all.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse), Description = "The list of descendant annotations.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetDescendants(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Descendants)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string descendants = "all",
        [FromQuery] string? continuationToken = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Reader, MachineScope.AnnotationRead);

        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {key}"));
        }

        switch (annotationKey.Type)
        {
            case AnnotationType.Responsibility:
            {
                if (descendants == "contexts")
                {
                    return new BadRequestObjectResult(new ErrorResponse("A responsibility has no contexts as descendants."));
                }

                break;
            }

            case AnnotationType.Unit:
            {
                if (descendants != "units-of-execution" && descendants != "all")
                {
                    return new BadRequestObjectResult(new ErrorResponse("An unit has only units-of-execution as descendants."));
                }

                break;
            }

            case AnnotationType.Subject:
            {
                if (descendants == "units")
                {
                    return new BadRequestObjectResult(new ErrorResponse("A subject has no units as descendants."));
                }

                break;
            }

            case AnnotationType.Usage:
            case AnnotationType.Context:
            {
                if (descendants != "executions" && descendants != "units-of-execution" && descendants != "all")
                {
                    return new BadRequestObjectResult(new ErrorResponse("An usage has only executions or units-of-execution as descendants."));
                }

                break;
            }

            case AnnotationType.Execution:
            {
                if (descendants != "units-of-execution" && descendants != "all")
                {
                    return new BadRequestObjectResult(new ErrorResponse("An execution has only units-of-execution as descendants."));
                }

                break;
            }

            case AnnotationType.UnitOfExecution:
                return new BadRequestObjectResult(new ErrorResponse("An unit of execution has no descendants."));

            default:
                return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {key}"));
        }

        AnnotationType[]? types = descendants switch
        {
            "usages" => [AnnotationType.Usage],
            "contexts" => [AnnotationType.Context],
            "executions" => [AnnotationType.Execution],
            "units" => [AnnotationType.Unit],
            "units-of-execution" => [AnnotationType.UnitOfExecution],
            "all" => [AnnotationType.Unit, AnnotationType.Usage, AnnotationType.Context, AnnotationType.Execution, AnnotationType.UnitOfExecution],
            _ => null,
        };

        if (types is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("Invalid type of descendants, allowed types are: usages, contexts, executions, units, units-of-execution, or all."));
        }

        // the descendants in the inheritance graph: the service finds their partitions and key prefixes
        var dataRequest = new AnnotationsRequest
        {
            ContinuationToken = continuationToken,
            IncludeInactive = includeInactive,
            Deleted = DeletedFilter(includeDeleted),
            Organization = organization,
            Project = project,
            View = view,
            Types = types,
            DescendantsOf = annotationKey,
        };

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response);
    }

    [Function(nameof(SetAnnotation))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.SetAnnotation, Definitions.Tags.Annotations, Description = "Creates the annotation (with its missing ancestors), updates it when it is active, or re-creates it when it is deleted (a deleted ancestor is re-created too). The body must carry the type-specific members (SubjectKey, ResponsibilityKey, UnitType, ...); the server-owned members (Created, Modified, Deletion, the relation arrays) are ignored.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(Annotation), Description = "The annotation to create or update.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Annotation), Description = "The stored annotation.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseConcurrentChange)]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationWrite)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Definitions.Methods.Put, Route = Definitions.Routes.Annotations.V1.Annotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Contributor, MachineScope.AnnotationWrite);

        // The body is read once, into the runtime type of its AnnotationType: the type-specific members
        // (SubjectKey, UnitType, ...) are required there, so a body without them is rejected instead of stored. Its
        // bytes are parsed as they came, so a body that is not UTF-8 is a 400 rather than replacement characters.
        Annotation annotation;
        using var body = new MemoryStream();

        if (!await request.TryCopyBodyAsync(body, RequestBodyLimits.Annotation, request.FunctionContext.CancellationToken))
        {
            return RequestBodyExtensions.BodyTooLarge(RequestBodyLimits.Annotation);
        }

        try
        {
            using var document = JsonDocument.Parse(body.WithoutByteOrderMark());

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new BadRequestObjectResult(new ErrorResponse("No annotation object in body."));
            }

            if (!document.RootElement.TryGetProperty(nameof(Annotation.AnnotationType), out var typeElement)
                || !Enum.TryParse(typeElement.ToString(), ignoreCase: true, out AnnotationType type)
                || !Enum.IsDefined(type))
            {
                return new BadRequestObjectResult(new ErrorResponse("The annotation has no known AnnotationType."));
            }

            var deserializedObject = document.RootElement.Deserialize(AnnotationTypes.GetRuntimeType(type), AnnotationBodyOptions);

            if (deserializedObject is null)
            {
                return new BadRequestObjectResult(new ErrorResponse("No annotation object in body."));
            }

            annotation = (Annotation)deserializedObject;
        }
        catch (JsonException exception)
        {
            return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation object in body: {exception.Message}"));
        }

        if (!AnnotationKey.TryParse(key, out _))
        {
            return InvalidKey(key);
        }

        annotation = annotation with
        {
            ProjectName = project,
            ViewName = view,
            AnnotationKey = key,
        };

        try
        {
            // POST and PUT both store the annotation: a new key is created with its missing ancestors, an active one
            // is updated, and a deleted one is re-created. The server-owned members of the body are ignored.
            var stored = await _annotations.SetAnnotationAsync(organization, annotation, request.GetAuditActor());
            return new OkObjectResult(stored);
        }
        catch (StorytellerException exception)
        {
            return ErrorResponseMapping.ToActionResult(exception);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to set annotation.");
            return new BadRequestObjectResult(exception.ToErrorResponse());
        }
    }

    [Function(nameof(SetAnnotations))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.SetAnnotations, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(List<Annotation>), Description = "The annotations to create.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(List<Annotation>), Description = "The created annotations, with the ancestors created for them; existing keys are skipped.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenManagementAnnotationManage)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Annotations.V1.Annotations)]
        HttpRequestData request,
        string organization,
        string project,
        string view)
    {
        // Each element is read as the annotation that would be stored. A list of base Annotation would drop
        // UnitType and UnitDefinition, and a body that is not JSON still names no key.
        var (elements, invalidBody) = await request.ReadJsonBodyAsync<List<JsonElement>>(_bodyJsonOptions, "a list of annotations", RequestBodyLimits.Annotations);

        // The stored key is authorized before any 400 of the body, so a caller without access gets 403 first.
        // Deserialization keeps the last duplicate member. When an element cannot be read, the same last
        // AnnotationKey is taken from the raw element, so one broken element cannot hide a key outside the restriction.
        // A body that cannot be read names no key, which the restriction of a machine lets through to the 400.
        List<Annotation>? annotations = null;
        JsonException? invalidElement = null;

        if (elements is { } body && body.All(element => element.ValueKind != JsonValueKind.Null))
        {
            try
            {
                annotations = body.Select(DeserializeAnnotationElement).ToList();
            }
            catch (JsonException exception)
            {
                invalidElement = exception;
            }
        }

        var keys = annotations is not null
            ? annotations.Select(annotation => annotation.AnnotationKey)
            : (elements ?? []).Select(AnnotationKeyOf).Where(key => key is not null).Select(key => key!);
        var restriction = await request.CheckAccessToAnnotationsAsync(_access, organization, project, keys, AccountRole.Contributor, MachineScope.AnnotationWrite, MachineScope.AnnotationManage);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (elements is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("The annotations are missing."));
        }

        // The projection below would dereference a null entry.
        if (elements.Any(element => element.ValueKind == JsonValueKind.Null))
        {
            return new BadRequestObjectResult(new ErrorResponse("The annotations must not contain null entries."));
        }

        if (invalidElement is not null)
        {
            return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation object in body: {invalidElement.Message}"));
        }

        var annotationsToProcess = annotations!
            .Select(a => a is Unit unit
                ? (Annotation)(unit with { ProjectName = project, ViewName = view })
                : a with { ProjectName = project, ViewName = view })
            .ToList();

        if (annotationsToProcess.Count > ApiConstants.MaxInputItems)
        {
            return new BadRequestObjectResult(new ErrorResponse($"The number of annotations to create is too large, maximum allowed is {ApiConstants.MaxInputItems}."));
        }

        var createdAnnotations = await _annotations.CreateAnnotationsAsync(organization, annotationsToProcess, request.GetAuditActor());

        // The missing ancestors are created too; a machine restricted to an annotation sees only what it may address.
        if (restriction.IsRestricted)
        {
            createdAnnotations = createdAnnotations.Where(annotation => restriction.Allows(annotation.AnnotationKey)).ToList();
        }

        return new OkObjectResult(createdAnnotations);
    }

    [Function(nameof(SetAnnotationsSimple))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.SetAnnotationsSimple, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(List<string>), Description = "The keys, or compact lines, of the annotations to create.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(List<Annotation>), Description = "The created annotations, with the ancestors created for them; existing keys are skipped.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenManagementAnnotationManage)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetAnnotationsSimple(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Annotations.V1.AnnotationsSimple)]
        HttpRequestData request,
        string organization,
        string project,
        string view)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor, MachineScope.AnnotationWrite, MachineScope.AnnotationManage);
        var (annotations, invalidBody) = await request.ReadJsonBodyAsync<List<string>>(_bodyJsonOptions, "a list of annotation keys or lines", RequestBodyLimits.AnnotationKeys);

        if (invalidBody is not null)
        {
            return invalidBody;
        }

        if (annotations is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("The annotations are missing."));
        }

        if (annotations.Count > ApiConstants.MaxInputItems)
        {
            return new BadRequestObjectResult(new ErrorResponse($"The number of annotations to create is too large, maximum allowed is {ApiConstants.MaxInputItems}."));
        }

        // the lines are created in the project and view of the route, the one the access check covered
        var createdAnnotations = await _annotations.CreateAnnotationsFromStringAsync(organization, project, view, annotations, request.GetAuditActor());
        return new OkObjectResult(createdAnnotations);
    }

    [Function(nameof(DeleteAnnotation))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.DeleteAnnotation, Definitions.Tags.Annotations, Description = "Deletes the annotation and all its descendants. They can be restored until an Administrator deletes them permanently or the retention runs out; a repeated delete returns the existing deletion.")]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationDeletionResult), Description = "The deletion and the deleted keys, descendants first and the annotation last.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The annotation doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.Conflict, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseConcurrentChange)]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenManagementAnnotationManage)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Annotations.V1.Annotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        await request.CheckAccessToAnnotationAsync(_access, organization, project, key, AccountRole.Contributor, MachineScope.AnnotationWrite, MachineScope.AnnotationManage);

        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {key}"));
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);

        try
        {
            var result = await _annotations.DeleteAnnotationAsync(fullKey, request.GetAuditActor());
            return new OkObjectResult(result);
        }
        catch (NotFoundException)
        {
            return new NotFoundResult();
        }
    }

    [Function(nameof(GetResponsibilities))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetResponsibilities, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.NameQuery, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name query to filter the responsibilities.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Responsibility>), Description = "The list of responsibilities.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetResponsibilities(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Responsibilities)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? nameQuery = null,
        [FromQuery] string? continuationToken = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.AnnotationRead);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types =
            [
                AnnotationType.Responsibility
            ],
            Conditions = conditions,
            ContinuationToken = continuationToken,
            IncludeInactive = includeInactive,
            Deleted = DeletedFilter(includeDeleted),
            Organization = organization,
            Project = project,
            View = view,
        };

        if (!string.IsNullOrWhiteSpace(nameQuery))
        {
            nameQuery = nameQuery.Trim();

            if (nameQuery.StartsWith('^') || nameQuery.EndsWith('$'))
            {
                var regex = new Regex(nameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Responsibility>
                {
                    Predicate = r => regex.IsMatch(r.Name),
                });
            }
            else if (nameQuery.StartsWith('%') || nameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Responsibility>
                {
                    Predicate = r => r.Name.Contains(nameQuery),
                });
            }
            else
            {
                dataRequest.PartitionKey = PartitionKeys.GetResponsibility(project, nameQuery);
                conditions.Add(new AnnotationsRequest.Condition<Responsibility>
                {
                    Predicate = r => r.Name == nameQuery,
                });
            }
        }

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response.AsTyped<Responsibility>());
    }

    [Function(nameof(GetSubjects))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetSubjects, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.NameQuery, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name query to filter the subjects.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Subject>), Description = "The list of subjects.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetSubjects(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Subjects)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? nameQuery = null,
        [FromQuery] string? continuationToken = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.AnnotationRead);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types =
            [
                AnnotationType.Subject
            ],
            Conditions = conditions,
            ContinuationToken = continuationToken,
            IncludeInactive = includeInactive,
            Deleted = DeletedFilter(includeDeleted),
            Organization = organization,
            Project = project,
            View = view,
        };

        if (!string.IsNullOrWhiteSpace(nameQuery))
        {
            nameQuery = nameQuery.Trim();

            if (nameQuery.StartsWith('^') || nameQuery.EndsWith('$'))
            {
                var regex = new Regex(nameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Subject>
                {
                    Predicate = s => regex.IsMatch(s.Name),
                });
            }
            else if (nameQuery.StartsWith('%') || nameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Subject>
                {
                    Predicate = s => s.Name.Contains(nameQuery),
                });
            }
            else
            {
                dataRequest.PartitionKey = PartitionKeys.GetSubject(project, nameQuery);
                conditions.Add(new AnnotationsRequest.Condition<Subject>
                {
                    Predicate = s => s.Name == nameQuery,
                });
            }
        }

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response.AsTyped<Subject>());
    }

    [Function(nameof(GetUsages))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetUsages, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter("responsibilityNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name query to filter the usages by responsibility.")]
    [OpenApiParameter("subjectNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name query to filter the usages by subject.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Usage>), Description = "The list of usages.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetUsages(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Usages)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? responsibilityNameQuery = null,
        [FromQuery] string? subjectNameQuery = null,
        [FromQuery] string? continuationToken = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.AnnotationRead);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types =
            [
                AnnotationType.Usage
            ],
            Conditions = conditions,
            ContinuationToken = continuationToken,
            IncludeInactive = includeInactive,
            Deleted = DeletedFilter(includeDeleted),
            Organization = organization,
            Project = project,
            View = view,
        };

        if (!string.IsNullOrWhiteSpace(responsibilityNameQuery))
        {
            responsibilityNameQuery = responsibilityNameQuery.Trim();

            if (responsibilityNameQuery.StartsWith('^') || responsibilityNameQuery.EndsWith('$'))
            {
                var regex = new Regex(responsibilityNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Usage>
                {
                    Predicate = u => regex.IsMatch(u.ResponsibilityName),
                });
            }
            else if (responsibilityNameQuery.StartsWith('%') || responsibilityNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Usage>
                {
                    Predicate = u => u.ResponsibilityName.Contains(responsibilityNameQuery),
                });
            }
            else
            {
                dataRequest.PartitionKey = PartitionKeys.GetResponsibility(project, responsibilityNameQuery);
                conditions.Add(new AnnotationsRequest.Condition<Usage>
                {
                    Predicate = u => u.ResponsibilityName == responsibilityNameQuery,
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(subjectNameQuery))
        {
            subjectNameQuery = subjectNameQuery.Trim();

            if (subjectNameQuery.StartsWith('^') || subjectNameQuery.EndsWith('$'))
            {
                var regex = new Regex(subjectNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Usage>
                {
                    Predicate = u => regex.IsMatch(u.SubjectName),
                });
            }
            else if (subjectNameQuery.StartsWith('%') || subjectNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Usage>
                {
                    Predicate = u => u.SubjectName.Contains(subjectNameQuery),
                });
            }
            else
            {
                string subjectKey = AnnotationKey.CreateSubject(subjectNameQuery);
                conditions.Add(new AnnotationsRequest.Condition<Usage>
                {
                    Predicate = u => u.SubjectKey == subjectKey,
                });
            }
        }

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response.AsTyped<Usage>());
    }

    [Function(nameof(GetContexts))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetContexts, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter("subjectNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name query to filter the contexts by subject.")]
    [OpenApiParameter(Definitions.Parameters.NameQuery, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name query to filter the contexts.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Context>), Description = "The list of contexts.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetContexts(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Contexts)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? subjectNameQuery = null,
        [FromQuery] string? nameQuery = null,
        [FromQuery] string? continuationToken = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.AnnotationRead);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types =
            [
                AnnotationType.Context
            ],
            Conditions = conditions,
            ContinuationToken = continuationToken,
            IncludeInactive = includeInactive,
            Deleted = DeletedFilter(includeDeleted),
            Organization = organization,
            Project = project,
            View = view,
        };

        if (!string.IsNullOrWhiteSpace(subjectNameQuery))
        {
            subjectNameQuery = subjectNameQuery.Trim();

            if (subjectNameQuery.StartsWith('^') || subjectNameQuery.EndsWith('$'))
            {
                var regex = new Regex(subjectNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Context>
                {
                    Predicate = u => regex.IsMatch(u.SubjectName),
                });
            }
            else if (subjectNameQuery.StartsWith('%') || subjectNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Context>
                {
                    Predicate = u => u.SubjectName.Contains(subjectNameQuery),
                });
            }
            else
            {
                dataRequest.PartitionKey = PartitionKeys.GetSubject(project, subjectNameQuery);
                conditions.Add(new AnnotationsRequest.Condition<Context>
                {
                    Predicate = u => u.SubjectName == subjectNameQuery,
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(nameQuery))
        {
            nameQuery = nameQuery.Trim();

            if (nameQuery.StartsWith('^') || nameQuery.EndsWith('$'))
            {
                var regex = new Regex(nameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Context>
                {
                    Predicate = u => regex.IsMatch(u.Name),
                });
            }
            else if (nameQuery.StartsWith('%') || nameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Context>
                {
                    Predicate = u => u.Name.Contains(nameQuery),
                });
            }
            else
            {
                conditions.Add(new AnnotationsRequest.Condition<Context>
                {
                    Predicate = u => u.Name == nameQuery,
                });
            }
        }

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response.AsTyped<Context>());
    }

    [Function(nameof(GetExecutions))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetExecutions, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter("responsibilityNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name query to filter the executions by responsibility.")]
    [OpenApiParameter("subjectNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name query to filter the executions by subject.")]
    [OpenApiParameter("contextNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The name query to filter the executions by context.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Execution>), Description = "The list of executions.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetExecutions(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Executions)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? responsibilityNameQuery = null,
        [FromQuery] string? subjectNameQuery = null,
        [FromQuery] string? contextNameQuery = null,
        [FromQuery] string? continuationToken = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.AnnotationRead);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types =
            [
                AnnotationType.Execution
            ],
            Conditions = conditions,
            ContinuationToken = continuationToken,
            IncludeInactive = includeInactive,
            Deleted = DeletedFilter(includeDeleted),
            Organization = organization,
            Project = project,
            View = view,
        };

        if (!string.IsNullOrWhiteSpace(responsibilityNameQuery))
        {
            responsibilityNameQuery = responsibilityNameQuery.Trim();

            if (responsibilityNameQuery.StartsWith('^') || responsibilityNameQuery.EndsWith('$'))
            {
                var regex = new Regex(responsibilityNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Execution>
                {
                    Predicate = u => regex.IsMatch(u.ResponsibilityName),
                });
            }
            else if (responsibilityNameQuery.StartsWith('%') || responsibilityNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Execution>
                {
                    Predicate = u => u.ResponsibilityName.Contains(responsibilityNameQuery),
                });
            }
            else
            {
                dataRequest.PartitionKey = PartitionKeys.GetResponsibility(project, responsibilityNameQuery);
                conditions.Add(new AnnotationsRequest.Condition<Execution>
                {
                    Predicate = u => u.ResponsibilityName == responsibilityNameQuery,
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(subjectNameQuery))
        {
            subjectNameQuery = subjectNameQuery.Trim();

            if (subjectNameQuery.StartsWith('^') || subjectNameQuery.EndsWith('$'))
            {
                var regex = new Regex(subjectNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Execution>
                {
                    Predicate = u => regex.IsMatch(u.SubjectName),
                });
            }
            else if (subjectNameQuery.StartsWith('%') || subjectNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Execution>
                {
                    Predicate = u => u.SubjectName.Contains(subjectNameQuery),
                });
            }
            else
            {
                conditions.Add(new AnnotationsRequest.Condition<Execution>
                {
                    Predicate = u => u.SubjectName == subjectNameQuery,
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(contextNameQuery))
        {
            contextNameQuery = contextNameQuery.Trim();

            if (contextNameQuery.StartsWith('^') || contextNameQuery.EndsWith('$'))
            {
                var regex = new Regex(contextNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Execution>
                {
                    Predicate = u => regex.IsMatch(u.Name),
                });
            }
            else if (contextNameQuery.StartsWith('%') || contextNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Execution>
                {
                    Predicate = u => u.Name.Contains(contextNameQuery),
                });
            }
            else
            {
                conditions.Add(new AnnotationsRequest.Condition<Execution>
                {
                    Predicate = u => u.Name == contextNameQuery,
                });
            }
        }

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response.AsTyped<Execution>());
    }

    [Function(nameof(GetUnits))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetUnits, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter("responsibilityNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The responsibility name query to filter the units.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Unit>), Description = "The list of units.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetUnits(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Units)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? responsibilityNameQuery = null,
        [FromQuery] string? continuationToken = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.AnnotationRead);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types =
            [
                AnnotationType.Unit
            ],
            Conditions = conditions,
            ContinuationToken = continuationToken,
            IncludeInactive = includeInactive,
            Deleted = DeletedFilter(includeDeleted),
            Organization = organization,
            Project = project,
            View = view,
        };

        if (!string.IsNullOrWhiteSpace(responsibilityNameQuery))
        {
            responsibilityNameQuery = responsibilityNameQuery.Trim();

            if (responsibilityNameQuery.StartsWith('^') || responsibilityNameQuery.EndsWith('$'))
            {
                var regex = new Regex(responsibilityNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<Unit>
                {
                    Predicate = u => regex.IsMatch(u.ResponsibilityName),
                });
            }
            else if (responsibilityNameQuery.StartsWith('%') || responsibilityNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<Unit>
                {
                    Predicate = u => u.ResponsibilityName.Contains(responsibilityNameQuery),
                });
            }
            else
            {
                dataRequest.PartitionKey = PartitionKeys.GetResponsibility(project, responsibilityNameQuery);
                conditions.Add(new AnnotationsRequest.Condition<Unit>
                {
                    Predicate = u => u.ResponsibilityName == responsibilityNameQuery,
                });
            }
        }

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response.AsTyped<Unit>());
    }

    [Function(nameof(GetUnitsOfExecution))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.GetUnitsOfExecution, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter("responsibilityNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The responsibility name query to filter the units of execution.")]
    [OpenApiParameter("unitNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The unit name query to filter the units of execution.")]
    [OpenApiParameter("subjectNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The subject name query to filter the units of execution.")]
    [OpenApiParameter("contextNameQuery", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The context name query to filter the units of execution.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiParameter(Definitions.Parameters.IncludeInactive, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeInactive)]
    [OpenApiParameter(Definitions.Parameters.IncludeDeleted, In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = Definitions.Descriptions.IncludeDeleted)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<UnitOfExecution>), Description = "The list of units of execution.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized)]
    [OpenApiResponseWithBody(HttpStatusCode.Forbidden, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseForbiddenAnnotationRead)]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetUnitsOfExecution(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.UnitsOfExecution)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? responsibilityNameQuery = null,
        [FromQuery] string? unitNameQuery = null,
        [FromQuery] string? subjectNameQuery = null,
        [FromQuery] string? contextNameQuery = null,
        [FromQuery] string? continuationToken = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeDeleted = false)
    {
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Reader, MachineScope.AnnotationRead);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types =
            [
                AnnotationType.UnitOfExecution
            ],
            Conditions = conditions,
            ContinuationToken = continuationToken,
            IncludeInactive = includeInactive,
            Deleted = DeletedFilter(includeDeleted),
            Organization = organization,
            Project = project,
            View = view,
        };

        if (!string.IsNullOrWhiteSpace(responsibilityNameQuery))
        {
            responsibilityNameQuery = responsibilityNameQuery.Trim();

            if (responsibilityNameQuery.StartsWith('^') || responsibilityNameQuery.EndsWith('$'))
            {
                var regex = new Regex(responsibilityNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => regex.IsMatch(u.ResponsibilityName),
                });
            }
            else if (responsibilityNameQuery.StartsWith('%') || responsibilityNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => u.ResponsibilityName.Contains(responsibilityNameQuery),
                });
            }
            else
            {
                dataRequest.PartitionKey = PartitionKeys.GetResponsibility(project, responsibilityNameQuery);
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => u.ResponsibilityName == responsibilityNameQuery,
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(unitNameQuery))
        {
            unitNameQuery = unitNameQuery.Trim();

            if (unitNameQuery.StartsWith('^') || unitNameQuery.EndsWith('$'))
            {
                var regex = new Regex(unitNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => regex.IsMatch(u.UnitName),
                });
            }
            else if (unitNameQuery.StartsWith('%') || unitNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => u.UnitName.Contains(unitNameQuery),
                });
            }
            else
            {
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => u.UnitName == unitNameQuery,
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(subjectNameQuery))
        {
            subjectNameQuery = subjectNameQuery.Trim();

            if (subjectNameQuery.StartsWith('^') || subjectNameQuery.EndsWith('$'))
            {
                var regex = new Regex(subjectNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => regex.IsMatch(u.SubjectName),
                });
            }
            else if (subjectNameQuery.StartsWith('%') || subjectNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => u.SubjectName.Contains(subjectNameQuery),
                });
            }
            else
            {
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => u.SubjectName == subjectNameQuery,
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(contextNameQuery))
        {
            contextNameQuery = contextNameQuery.Trim();

            if (contextNameQuery.StartsWith('^') || contextNameQuery.EndsWith('$'))
            {
                var regex = new Regex(contextNameQuery, RegexOptions.Compiled);
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => regex.IsMatch(u.ContextName),
                });
            }
            else if (contextNameQuery.StartsWith('%') || contextNameQuery.EndsWith('%'))
            {
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => u.ContextName.Contains(contextNameQuery),
                });
            }
            else
            {
                conditions.Add(new AnnotationsRequest.Condition<UnitOfExecution>
                {
                    Predicate = u => u.ContextName == contextNameQuery,
                });
            }
        }

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response.AsTyped<UnitOfExecution>());
    }

    private static AnnotationDeletedFilter DeletedFilter(bool includeDeleted)
    {
        return includeDeleted ? AnnotationDeletedFilter.Include : AnnotationDeletedFilter.Exclude;
    }

    // A unit element that carries a trigger is the concrete Unit, so the values reach normalization.
    // Every other element stays a base Annotation and keeps today's defaults.
    private static Annotation DeserializeAnnotationElement(JsonElement element)
    {
        var runtimeType = IsUnitElement(element) && HasUnitMember(element) ? typeof(Unit) : typeof(Annotation);
        var deserialized = element.Deserialize(runtimeType, AnnotationBodyOptions);

        if (deserialized is not Annotation annotation)
        {
            throw new JsonException("No annotation object in body.");
        }

        return annotation;
    }

    private static bool IsUnitElement(JsonElement element)
    {
        if (!TryGetProperty(element, nameof(Annotation.AnnotationType), out var type) || type.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return Enum.TryParse(type.GetString(), ignoreCase: true, out AnnotationType annotationType)
            && annotationType == AnnotationType.Unit;
    }

    private static bool HasUnitMember(JsonElement element)
    {
        return TryGetProperty(element, nameof(Unit.UnitType), out _)
            || TryGetProperty(element, nameof(Unit.UnitDefinition), out _);
    }

    private static string? AnnotationKeyOf(JsonElement element)
    {
        return TryGetProperty(element, nameof(Annotation.AnnotationKey), out var key) && key.ValueKind == JsonValueKind.String
            ? key.GetString()
            : null;
    }

    // The last case-insensitive match wins, as deserialization does. The value stays valid for the document.
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        var found = false;
        value = default;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    found = true;
                }
            }
        }

        return found;
    }
}
