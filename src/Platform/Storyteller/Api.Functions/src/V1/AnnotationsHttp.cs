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
using Microsoft.OpenApi.Models;

using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace _42.Platform.Storyteller.Api.V1;

public class AnnotationsHttp
{
    private readonly IAnnotationService _annotations;
    private readonly IAccessService _access;
    private readonly ILogger<AnnotationsHttp> _logger;

    public AnnotationsHttp(
        IAnnotationService annotations,
        IAccessService access,
        ILogger<AnnotationsHttp> logger)
    {
        _annotations = annotations;
        _access = access;
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
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse), Description = "The list of annotations.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Annotations)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? continuationToken = null)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var dataRequest = new AnnotationsRequest
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
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Annotation), Description = "The requested annotation.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The requested annotation doesn't exist.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Annotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {key}"));
        }

        var fullKey = FullKey.Create(annotationKey, organization, project, view);
        var annotation = await _annotations.GetAnnotationAsync(fullKey);

        if (annotation is null)
        {
            return new NotFoundResult();
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
    [OpenApiParameter(Definitions.Parameters.Descendants, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The type of descendants to retrieve, possible value is: usages, contexts, executions, or all.")]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse), Description = "The list of descendant annotations.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetDescendants(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Descendants)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        string descendants = "all",
        [FromQuery] string? continuationToken = null)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        if (!AnnotationKey.TryParse(key, out var annotationKey))
        {
            return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {key}"));
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
                    return new BadRequestObjectResult(new ErrorResponse("An usage has only executions as descendants."));
                }

                break;
            }

            case AnnotationType.Execution:
                return new BadRequestObjectResult(new ErrorResponse("An execution has no descendants."));

            default:
                return new BadRequestObjectResult(new ErrorResponse($"Invalid annotation key: {key}"));
        }

        var dataRequest = new AnnotationsRequest
        {
            ContinuationToken = continuationToken,
            Organization = organization,
            Project = project,
            View = view,
        };

        switch (descendants)
        {
            case "usages":
                dataRequest.Types = new[] { AnnotationType.Usage };
                break;

            case "contexts":
                dataRequest.Types = new[] { AnnotationType.Context };
                break;

            case "executions":
                dataRequest.Types = new[] { AnnotationType.Execution };
                break;

            case "all":
                dataRequest.Types = new[]
                {
                    AnnotationType.Usage,
                    AnnotationType.Execution,
                };
                break;

            default:
                return new BadRequestObjectResult(new ErrorResponse("Invalid type of descendants, allowed types are: usages, contexts, executions, or all."));
        }

        switch (annotationKey.Type)
        {
            case AnnotationType.Responsibility:
            {
                dataRequest.PartitionKey = PartitionKeys.GetResponsibility(project, annotationKey.ResponsibilityName);
                dataRequest.Conditions = new AnnotationsRequest.ICondition[]
                {
                    new AnnotationsRequest.Condition<Usage> { Predicate = u => u.ResponsibilityKey == key, },
                    new AnnotationsRequest.Condition<Execution> { Predicate = e => e.ResponsibilityKey == key, },
                };
                break;
            }

            case AnnotationType.Subject:
            {
                dataRequest.Conditions = new AnnotationsRequest.ICondition[]
                {
                    new AnnotationsRequest.Condition<Usage> { Predicate = u => u.SubjectKey == key, },
                    new AnnotationsRequest.Condition<Context> { Predicate = c => c.SubjectKey == key, },
                    new AnnotationsRequest.Condition<Execution> { Predicate = e => e.SubjectKey == key, },
                };
                break;
            }

            case AnnotationType.Usage:
            {
                dataRequest.PartitionKey = PartitionKeys.GetResponsibility(project, annotationKey.ResponsibilityName);
                string subjectKey = annotationKey.GetSubjectKey();
                string responsibilityKey = annotationKey.GetResponsibilityKey();

                dataRequest.Conditions = new AnnotationsRequest.ICondition[]
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

                dataRequest.Conditions = new AnnotationsRequest.ICondition[]
                {
                    new AnnotationsRequest.Condition<Execution>
                    {
                        Predicate = e => e.SubjectKey == subjectKey && e.ContextKey == key,
                    },
                };
                break;
            }

            default:
                return new BadRequestObjectResult(new ErrorResponse("Invalid annotation key, descendants can be returned only of a responsibility, subject, context or usage."));
        }

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response);
    }

    [Function(nameof(SetAnnotation))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.SetAnnotation, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(Annotation), Description = "The annotation to create or update.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Annotation), Description = "The created or updated annotation.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Definitions.Methods.Put, Route = Definitions.Routes.Annotations.V1.Annotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key,
        [FromBody] Annotation annotation)
    {
        request.CheckScope(Scopes.Annotation.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        var annotationType = AnnotationTypes.GetRuntimeType(annotation.AnnotationType);
        using var reader = new StreamReader(request.Body);
        var deserializedObject = await JsonSerializer.DeserializeAsync(
            request.Body,
            annotationType,
            new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true,
            });

        if (deserializedObject is null)
        {
            return new BadRequestObjectResult(new ErrorResponse("Invalid annotation object in body."));
        }

        // TODO: [P1] set system properties and validate the annotation
        var typedAnnotation = (Annotation)deserializedObject;
        typedAnnotation = typedAnnotation with
        {
            ProjectName = project,
            ViewName = view,
            AnnotationKey = key,
        };

        try
        {
            await _annotations.CreateOrUpdateAnnotationAsync(organization, typedAnnotation);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to set annotation.");
            return new BadRequestObjectResult(exception.ToErrorResponse());
        }

        return new OkObjectResult(typedAnnotation);
    }

    [Function(nameof(SetAnnotations))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.SetAnnotations, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(List<Annotation>), Description = "The annotations to create.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Guid), Description = "The id of the asynchronous create operation.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Annotations.V1.Annotations)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromBody] List<Annotation> annotations)
    {
        request.CheckScope(Scopes.Annotation.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        // TODO: [P1] implement
        throw new NotImplementedException();
    }

    [Function(nameof(SetAnnotationsSimple))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.SetAnnotationsSimple, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiRequestBody(Definitions.ContentTypes.Json, typeof(List<Annotation>), Description = "The annotations to create.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Guid), Description = "The id of the asynchronous create operation.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> SetAnnotationsSimple(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Post, Route = Definitions.Routes.Annotations.V1.AnnotationsSimple)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromBody] List<Annotation> annotations)
    {
        request.CheckScope(Scopes.Annotation.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        // TODO: [P1] implement
        throw new NotImplementedException();
    }

    [Function(nameof(DeleteAnnotation))]
    [OpenApiOperation(Definitions.RouteIds.Annotations.DeleteAnnotation, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.Key, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Key)]
    [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Acknowledge of the deletion.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NotFound, Description = "The annotation doesn't exist.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Write}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> DeleteAnnotation(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Delete, Route = Definitions.Routes.Annotations.V1.Annotation)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        string key)
    {
        request.CheckScope(Scopes.Annotation.Write, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project, AccountRole.Contributor);

        // TODO: [P1] implement
        throw new NotImplementedException();
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
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Responsibility>), Description = "The list of responsibilities.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetResponsibilities(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Responsibilities)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? nameQuery = null,
        [FromQuery] string? continuationToken = null)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Responsibility,
            },
            Conditions = conditions,
            ContinuationToken = continuationToken,
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
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Subject>), Description = "The list of subjects.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetSubjects(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Subjects)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? nameQuery = null,
        [FromQuery] string? continuationToken = null)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Subject,
            },
            Conditions = conditions,
            ContinuationToken = continuationToken,
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
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Usage>), Description = "The list of usages.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetUsages(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Usages)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? responsibilityNameQuery = null,
        [FromQuery] string? subjectNameQuery = null,
        [FromQuery] string? continuationToken = null)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Usage,
            },
            Conditions = conditions,
            ContinuationToken = continuationToken,
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
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Context>), Description = "The list of contexts.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetContexts(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Contexts)]
        HttpRequestData request,
        string organization,
        string project,
        string view,
        [FromQuery] string? subjectNameQuery = null,
        [FromQuery] string? nameQuery = null,
        [FromQuery] string? continuationToken = null)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Context,
            },
            Conditions = conditions,
            ContinuationToken = continuationToken,
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
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(AnnotationsResponse<Execution>), Description = "The list of executions.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
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
        [FromQuery] string? continuationToken = null)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);
        await request.CheckAccessToProjectAsync(_access, organization, project);

        var conditions = new List<AnnotationsRequest.ICondition>();
        var dataRequest = new AnnotationsRequest
        {
            Types = new[]
            {
                AnnotationType.Execution,
            },
            Conditions = conditions,
            ContinuationToken = continuationToken,
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
}
