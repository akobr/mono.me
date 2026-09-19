using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// Adds the <c>mtls</c> security scheme to the OpenAPI document.
/// <para>
/// OpenAPI 3.1 defines <c>type: mutualTLS</c> natively, but the Azure Functions
/// OpenAPI extension uses 3.0.x which does not support it. We represent mTLS as
/// an <c>apiKey</c>-type scheme with header transport — this is a documentation
/// convention that existing generators (NSwag, Kiota, OpenAPI Generator) can parse
/// without errors, even though the actual authentication is transport-layer
/// (client certificate via TLS handshake or the <c>X-ARR-ClientCert</c> header).
/// </para>
/// <para>
/// SDK consumers configure client certificates on <c>HttpClientHandler</c> or
/// equivalent, not through a generated API method parameter. The scheme entry
/// serves as documentation and as a signal to operators that certain endpoints
/// accept or require client certificates.
/// </para>
/// </summary>
public class MtlsSecuritySchemeDocumentFilter : IDocumentFilter
{
    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

        if (!document.Components.SecuritySchemes.ContainsKey("mtls"))
        {
            document.Components.SecuritySchemes["mtls"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-ARR-ClientCert",
                Description = "Mutual TLS client certificate authentication. "
                    + "The client certificate is presented during the TLS handshake (production) "
                    + "or via the X-ARR-ClientCert header (development). "
                    + "Configure the certificate on HttpClientHandler or equivalent — "
                    + "this is a transport-layer concern, not an API parameter.",
            };
        }
    }
}
