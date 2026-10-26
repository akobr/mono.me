using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

// The headers of the effective and composition reads, on their 200 and 304 responses.
public sealed class ConfigurationReadResponseHeaders : IOpenApiCustomResponseHeader
{
    public Dictionary<string, OpenApiHeader> Headers { get; set; } = new()
    {
        [HeaderNames.ETag] = StringHeader(Definitions.Descriptions.EntityTag),
        [HeaderNames.CacheControl] = StringHeader(Definitions.Descriptions.CacheControlConfigurationRead),
    };

    internal static OpenApiHeader StringHeader(string description)
    {
        return new OpenApiHeader
        {
            Description = description,
            Schema = new OpenApiSchema { Type = "string" },
        };
    }
}
