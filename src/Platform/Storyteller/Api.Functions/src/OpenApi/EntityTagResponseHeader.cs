using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

// The ETag of a conditional read, on its 200 and 304 responses.
public sealed class EntityTagResponseHeader : IOpenApiCustomResponseHeader
{
    public Dictionary<string, OpenApiHeader> Headers { get; set; } = new()
    {
        [HeaderNames.ETag] = new OpenApiHeader
        {
            Description = Definitions.Descriptions.EntityTag,
            Schema = new OpenApiSchema { Type = "string" },
        },
    };
}
