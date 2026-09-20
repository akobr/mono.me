using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// Adds the <c>apiKey</c> security scheme to the OpenAPI document for machine
/// access via structured API keys (<c>Authorization: ApiKey 2s.&lt;base64&gt;.&lt;secret&gt;</c>).
/// </summary>
public class ApiKeySecuritySchemeDocumentFilter : IDocumentFilter
{
    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

        if (!document.Components.SecuritySchemes.ContainsKey("apiKey"))
        {
            document.Components.SecuritySchemes["apiKey"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "Authorization",
                Description = "Machine access via structured API key. "
                    + "Format: 'ApiKey 2s.[base64(org:project:machineAccessId)].[secret]'. "
                    + "Issued through the machine access creation endpoint.",
            };
        }
    }
}
