using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace _42.Platform.Storyteller.Api.Controllers;

[Authorize]
[ApiVersion("1.0")]
[ApiController]
public class AnnotationsController : ControllerBase
{
    private readonly IAnnotationService _annotations;
    private readonly ILogger<AnnotationsController> _logger;

    public AnnotationsController(
        IAnnotationService annotations,
        ILogger<AnnotationsController> logger)
    {
        _annotations = annotations;
        _logger = logger;
    }

    [HttpGet("{project}/{view}/annotations", Name = "GetAnnotations")]
    [RequiredScope(Scopes.Annotation.Read)]
    [ProducesResponseType(typeof(AnnotationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAnnotations(
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName,
        [FromQuery] string? continuationToken = null)
    {
        var request = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Responsibility,
                AnnotationType.Subject,
                AnnotationType.Usage,
                AnnotationType.Context,
                AnnotationType.Execution,
            },
            ContinuationToken = continuationToken,
            Project = project,
            View = view,
        };

        var response = await _annotations.GetAnnotationsAsync(request);
        return new OkObjectResult(response);
    }

    [HttpGet("{project}/{view}/responsibilities", Name = "GetResponsibilities")]
    [RequiredScope(Scopes.Annotation.Read)]
    [ProducesResponseType(typeof(AnnotationsResponse<Responsibility>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetResponsibilities(
        [FromQuery] string? nameQuery = null,
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName,
        [FromQuery] string? continuationToken = null)
    {
        var request = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Responsibility,
            },
            ContinuationToken = continuationToken,
            Project = project,
            View = view,
        };

        var conditions = new List<AnnotationsRequest.ICondition>();

        if (AddNameQuery<Responsibility>(conditions, nameQuery, nameof(nameQuery), r => r.Name) is { IsExact: true } name)
        {
            request.PartitionKey = PartitionKeys.GetResponsibility(project, name.Value);
        }

        request.Conditions = conditions;

        var response = await _annotations.GetAnnotationsAsync(request);
        return new OkObjectResult(response.AsTyped<Responsibility>());
    }

    [HttpGet("{project}/{view}/subjects", Name = "GetSubjects")]
    [RequiredScope(Scopes.Annotation.Read)]
    [ProducesResponseType(typeof(AnnotationsResponse<Subject>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSubjects(
        [FromQuery] string? nameQuery = null,
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName,
        [FromQuery] string? continuationToken = null)
    {
        var request = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Subject,
            },
            ContinuationToken = continuationToken,
            Project = project,
            View = view,
        };

        var conditions = new List<AnnotationsRequest.ICondition>();

        if (AddNameQuery<Subject>(conditions, nameQuery, nameof(nameQuery), s => s.Name) is { IsExact: true } name)
        {
            request.PartitionKey = PartitionKeys.GetSubject(project, name.Value);
        }

        request.Conditions = conditions;

        var response = await _annotations.GetAnnotationsAsync(request);
        return new OkObjectResult(response.AsTyped<Subject>());
    }

    [HttpGet("{project}/{view}/usages", Name = "GetUsages")]
    [RequiredScope(Scopes.Annotation.Read)]
    [ProducesResponseType(typeof(AnnotationsResponse<Usage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUsages(
        [FromQuery] string? responsibilityNameQuery = null,
        [FromQuery] string? subjectNameQuery = null,
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName,
        [FromQuery] string? continuationToken = null)
    {
        var conditions = new List<AnnotationsRequest.ICondition>();
        var request = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Usage,
            },
            ContinuationToken = continuationToken,
            Project = project,
            View = view,
        };

        if (AddNameQuery<Usage>(conditions, responsibilityNameQuery, nameof(responsibilityNameQuery), u => u.ResponsibilityName) is { IsExact: true } responsibility)
        {
            request.PartitionKey = PartitionKeys.GetResponsibility(project, responsibility.Value);
        }

        AddNameQuery<Usage>(conditions, subjectNameQuery, nameof(subjectNameQuery), u => u.SubjectName);

        request.Conditions = conditions;
        var response = await _annotations.GetAnnotationsAsync(request);
        return new OkObjectResult(response.AsTyped<Usage>());
    }

    [HttpGet("{project}/{view}/contexts", Name = "GetContexts")]
    [RequiredScope(Scopes.Annotation.Read)]
    [ProducesResponseType(typeof(AnnotationsResponse<Context>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetContexts(
    [FromQuery] string? subjectNameQuery = null,
    [FromQuery] string? contextNameQuery = null,
    string project = Constants.DefaultProjectName,
    string view = Constants.DefaultViewName,
    [FromQuery] string? continuationToken = null)
    {
        var conditions = new List<AnnotationsRequest.ICondition>();
        var request = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Context,
            },
            ContinuationToken = continuationToken,
            Project = project,
            View = view,
        };

        if (AddNameQuery<Context>(conditions, subjectNameQuery, nameof(subjectNameQuery), c => c.SubjectName) is { IsExact: true } subject)
        {
            request.PartitionKey = PartitionKeys.GetSubject(project, subject.Value);
        }

        AddNameQuery<Context>(conditions, contextNameQuery, nameof(contextNameQuery), c => c.Name);

        request.Conditions = conditions;
        var response = await _annotations.GetAnnotationsAsync(request);
        return new OkObjectResult(response.AsTyped<Context>());
    }

    [HttpGet("{project}/{view}/executions", Name = "GetExecutions")]
    [RequiredScope(Scopes.Annotation.Read)]
    [ProducesResponseType(typeof(AnnotationsResponse<Execution>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetExecutions(
        [FromQuery] string? responsibilityNameQuery = null,
        [FromQuery] string? subjectNameQuery = null,
        [FromQuery] string? contextNameQuery = null,
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName,
        [FromQuery] string? continuationToken = null)
    {
        var conditions = new List<AnnotationsRequest.ICondition>();
        var request = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Execution,
            },
            ContinuationToken = continuationToken,
            Project = project,
            View = view,
        };

        if (AddNameQuery<Execution>(conditions, responsibilityNameQuery, nameof(responsibilityNameQuery), e => e.ResponsibilityName) is { IsExact: true } responsibility)
        {
            request.PartitionKey = PartitionKeys.GetResponsibility(project, responsibility.Value);
        }

        AddNameQuery<Execution>(conditions, subjectNameQuery, nameof(subjectNameQuery), e => e.SubjectName);
        AddNameQuery<Execution>(conditions, contextNameQuery, nameof(contextNameQuery), e => e.Name);

        request.Conditions = conditions;
        var response = await _annotations.GetAnnotationsAsync(request);
        return new OkObjectResult(response.AsTyped<Execution>());
    }

    [HttpGet("{project}/{view}/annotations/{key}/{descendants}", Name = "GetDescendants")]
    [RequiredScope(Scopes.Annotation.Read)]
    [ProducesResponseType(typeof(AnnotationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetDescendants(
        string key,
        string descendants = "all",
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName,
        [FromQuery] string? continuationToken = null)
    {
        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return new BadRequestObjectResult("Invalid annotation key.");
        }

        switch (annotationKey.Type)
        {
            case AnnotationType.Responsibility:
            case AnnotationType.Subject:
                break;

            case AnnotationType.Usage:
            case AnnotationType.Context:
            {
                if (descendants != "executions" && descendants != "all")
                {
                    return new BadRequestObjectResult("An usage has only executions as descendants.");
                }

                break;
            }

            case AnnotationType.Execution:
                return new BadRequestObjectResult("An execution has no descendants.");

            default:
                return new BadRequestObjectResult("Invalid annotation key.");
        }

        var request = new AnnotationsRequest
        {
            ContinuationToken = continuationToken,
            Project = project,
            View = view,
        };

        switch (descendants)
        {
            case "usages":
                request.Types = new[] { AnnotationType.Usage };
                break;

            case "contexts":
                request.Types = new[] { AnnotationType.Context };
                break;

            case "executions":
                request.Types = new[] { AnnotationType.Execution };
                break;

            case "all":
                request.Types = new[]
                {
                    AnnotationType.Usage,
                    AnnotationType.Execution,
                };
                break;

            default:
                return new BadRequestObjectResult("Invalid type of descendants, allowed types are: usages, contexts, executions, or all.");
        }

        switch (annotationKey.Type)
        {
            case AnnotationType.Responsibility:
            {
                request.PartitionKey = PartitionKeys.GetResponsibility(project, annotationKey.ResponsibilityName);
                request.Conditions = new AnnotationsRequest.ICondition[]
                {
                    new AnnotationsRequest.Condition<Usage>
                    {
                        Predicate = u => u.ResponsibilityKey == key,
                    },
                    new AnnotationsRequest.Condition<Execution>
                    {
                        Predicate = e => e.ResponsibilityKey == key,
                    },
                };
                break;
            }

            case AnnotationType.Subject:
            {
                request.Conditions = new AnnotationsRequest.ICondition[]
                {
                    new AnnotationsRequest.Condition<Usage>
                    {
                        Predicate = u => u.SubjectKey == key,
                    },
                    new AnnotationsRequest.Condition<Context>
                    {
                        Predicate = c => c.SubjectKey == key,
                    },
                    new AnnotationsRequest.Condition<Execution>
                    {
                        Predicate = e => e.SubjectKey == key,
                    },
                };
                break;
            }

            case AnnotationType.Usage:
            {
                request.PartitionKey = PartitionKeys.GetResponsibility(project, annotationKey.ResponsibilityName);
                string subjectKey = annotationKey.GetSubjectKey();
                string responsibilityKey = annotationKey.GetResponsibilityKey();

                request.Conditions = new AnnotationsRequest.ICondition[]
                {
                    new AnnotationsRequest.Condition<Execution>
                    {
                        Predicate = e => e.ResponsibilityKey == responsibilityKey && e.SubjectKey == subjectKey,
                    },
                };
                break;
            }

            case AnnotationType.Context:
            {
                string subjectKey = annotationKey.GetSubjectKey();

                request.Conditions = new AnnotationsRequest.ICondition[]
                {
                    new AnnotationsRequest.Condition<Execution>
                    {
                        Predicate = e => e.SubjectKey == subjectKey && e.ContextKey == key,
                    },
                };
                break;
            }

            default:
                return new BadRequestObjectResult("Invalid annotation key, descendants can be returned only of a responsibility, subject, context or usage.");
        }

        var response = await _annotations.GetAnnotationsAsync(request);
        return new OkObjectResult(response);
    }

    [HttpGet("{project}/{view}/annotations/{key}", Name = "GetAnnotation")]
    [RequiredScope(Scopes.Annotation.Read)]
    [ProducesResponseType(typeof(Annotation), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAnnotation(
        string key,
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName)
    {
        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return new BadRequestObjectResult("Invalid annotation key.");
        }

        var tenant = HttpContext.GetTenant();
        var fullKey = FullKey.Create(annotationKey, tenant, project, view);
        var annotation = await _annotations.GetAnnotationAsync(fullKey);

        if (annotation is null)
        {
            return new NotFoundObjectResult("Annotation not found.");
        }

        return new OkObjectResult(annotation);
    }

    [HttpPost("{project}/{view}/annotations/{key}", Name = "SetAnnotation")]
    [RequiredScope(Scopes.Annotation.Write)]
    [ProducesResponseType(typeof(Annotation), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetAnnotation(
        [FromBody] Annotation annotation,
        string key,
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName)
    {
        var annotationType = AnnotationTypes.GetRuntimeType(annotation.AnnotationType);
        using var reader = new StreamReader(Request.Body);
        var deserializedObject = await JsonSerializer.DeserializeAsync(
            Request.Body,
            annotationType,
            new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true,
            });

        if (deserializedObject is null)
        {
            return new BadRequestObjectResult("Invalid annotation object in body.");
        }

        // TODO: set system properties and validate the annotation
        var typedAnnotation = (Annotation)deserializedObject;
        var tenant = HttpContext.GetTenant();

        try
        {
            await _annotations.CreateAnnotationAsync(tenant, typedAnnotation, GetAuditActor());
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to set annotation.");
            return new BadRequestObjectResult(e.Message);
        }

        return new OkObjectResult(typedAnnotation);
    }

    [HttpPost("{project}/{view}/annotations", Name = "SetAnnotations")]
    [RequiredScope(Scopes.Annotation.Write)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetAnnotation([FromBody] List<Annotation> annotations)
    {
        var jobGuid = Guid.NewGuid();
        return new OkObjectResult(jobGuid);
    }

    [HttpPost("{project}/{view}/annotations/simple", Name = "SetSimpleAnnotations")]
    [RequiredScope(Scopes.Annotation.Write)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetSimpleAnnotation([FromBody] List<Annotation> annotations)
    {
        var jobGuid = Guid.NewGuid();
        return new OkObjectResult(jobGuid);
    }

    // The condition of a name query on one name of the listed type; answers the query, so that an exact one can narrow
    // the read to its partition, or null without one.
    private static NameQuery? AddNameQuery<TAnnotation>(List<AnnotationsRequest.ICondition> conditions, string? text, string parameter, Expression<Func<TAnnotation, string?>> name)
        where TAnnotation : Annotation
    {
        var query = NameQuery.Parse(text, parameter);

        if (query is not null)
        {
            conditions.Add(query.ToCondition(name));
        }

        return query;
    }

    private AuditActor GetAuditActor()
    {
        var id = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return id is null
            ? AuditActor.System
            : AuditActor.Account(id, User.FindFirst("name")?.Value);
    }

}
