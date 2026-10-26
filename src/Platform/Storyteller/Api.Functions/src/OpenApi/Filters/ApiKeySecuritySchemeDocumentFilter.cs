using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// Sets the <c>apiKey</c> security scheme of the OpenAPI document for machine access via structured API keys
/// (<c>Authorization: ApiKey 2s.&lt;base64url&gt;.&lt;secret&gt;</c>). The operations reference it through
/// <see cref="MachineSecurityRequirementDocumentFilter"/>.
/// </summary>
public class ApiKeySecuritySchemeDocumentFilter : IDocumentFilter
{
    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

        document.Components.SecuritySchemes[Definitions.SecuritySchemas.ApiKey] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = HeaderNames.Authorization,
            Description = Definitions.Descriptions.SecureApiKey,
        };
    }
}
