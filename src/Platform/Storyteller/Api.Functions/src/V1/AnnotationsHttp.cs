using System.Net;
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

public class AnnotationsHttp
{
    private readonly IAnnotationService _annotations;

    public AnnotationsHttp(IAnnotationService annotations)
    {
        _annotations = annotations;
    }

    [Function(nameof(GetAnnotations))]
    [OpenApiOperation(Definitions.Routes.Annotations.V1.Annotations, Definitions.Tags.Annotations)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Manual, SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = Definitions.Others.JWT, Description = Definitions.Descriptions.SecureManual)]
    [OpenApiSecurity(Definitions.SecuritySchemas.Integrated, SecuritySchemeType.OAuth2, Flows = typeof(OAuthFlows))]
    [OpenApiParameter(Definitions.Parameters.Organization, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Organization)]
    [OpenApiParameter(Definitions.Parameters.Project, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.Project)]
    [OpenApiParameter(Definitions.Parameters.View, In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = Definitions.Descriptions.View)]
    [OpenApiParameter(Definitions.Parameters.ContinuationToken, In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = Definitions.Descriptions.ContinuationToken)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, Definitions.ContentTypes.Json, typeof(Response), Description = "The list of annotations.")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseBadRequest)]
    [OpenApiResponseWithoutBody(HttpStatusCode.Unauthorized, Description = Definitions.Descriptions.ResponseUnauthorized + $"{Scopes.Annotation.Read}, {Scopes.Annotation.Write}, {Scopes.Default.Read}, {Scopes.Default.Write}")]
    [OpenApiResponseWithBody(HttpStatusCode.InternalServerError, Definitions.ContentTypes.Json, typeof(ErrorResponse), Description = Definitions.Descriptions.ResponseInternalServerError)]
    public async Task<IActionResult> GetAnnotations(
        [HttpTrigger(AuthorizationLevel.Anonymous, Definitions.Methods.Get, Route = Definitions.Routes.Annotations.V1.Annotations)]
        Continuation token,
        HttpRequestData request,
        string organization = Constants.DefaultTenantName,
        string project = Constants.DefaultProjectName,
        string view = Constants.DefaultViewName)
    {
        request.CheckScope(Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Default.Read, Scopes.Default.Write);

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
