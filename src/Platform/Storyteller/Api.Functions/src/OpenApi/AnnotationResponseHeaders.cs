using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

// The header of an annotation read and of the writes that answer its new version (POST, PATCH).
public sealed class AnnotationResponseHeaders : IOpenApiCustomResponseHeader
{
    public Dictionary<string, OpenApiHeader> Headers { get; set; } = new()
    {
        [HeaderNames.ETag] = ConfigurationReadResponseHeaders.StringHeader(Definitions.Descriptions.AnnotationEntityTag),
    };
}
