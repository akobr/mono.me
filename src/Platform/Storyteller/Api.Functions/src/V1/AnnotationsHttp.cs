using System.Net;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.OpenApi;
using _42.Platform.Storyteller.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.V1;

public class AnnotationsHttp
{
    public const string AnnotationTag = "Annotations";
    public const string AnnotationRoute = "v1/{organization}/{project}/{view}/annotations";

    private readonly IAnnotationService _annotations;

    public AnnotationsHttp(IAnnotationService annotations)
    {
        _annotations = annotations;
    }

    [Function("GetAnnotations")]
    [OpenApiOperation(AnnotationRoute, AnnotationTag)]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiSecurity("implicit_auth", SecuritySchemeType.OAuth2, Flows = typeof(ImplicitAuthFlow))]
    [OpenApiParameter("organization", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Target organization.")]
    [OpenApiParameter("project", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Target project.")]
    [OpenApiParameter("view", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The used view inside the project.")]
    [OpenApiParameter("continuationToken", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "The continuation token for multi page queries.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, ContentTypes.Json, typeof(Response), Description = "The list of annotations.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, ContentTypes.Json, typeof(ErrorResponse), Description = "Poorly worded request.")]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = "Authentication or authorization issues. Scope(s): " + "{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, ContentTypes.Json, typeof(ErrorResponse), Description = "Unexpected error occurred on the service.")]
    public async Task<IActionResult> GetAnnotationsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = AnnotationRoute)]
        Continuation token,
        HttpRequest request,
        string organization = Constants.DefaultTenantName,
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName)
    {
        var dataRequest = new Request
        {
            Types = new[]
            {
                AnnotationType.Responsibility,
                AnnotationType.Subject,
                AnnotationType.Usage,
                AnnotationType.Context,
                AnnotationType.Execution,
            },
            ContinuationToken = token.ContinuationToken,
            Organization = organization,
            Project = project,
            View = view,
        };

        var response = await _annotations.GetAnnotationsAsync(dataRequest);
        return new OkObjectResult(response);
    }
}
