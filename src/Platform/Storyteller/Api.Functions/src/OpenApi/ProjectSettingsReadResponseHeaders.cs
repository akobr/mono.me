using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

// The headers of a read of project settings, on its 200 and 304 responses.
public sealed class ProjectSettingsReadResponseHeaders : IOpenApiCustomResponseHeader
{
    public Dictionary<string, OpenApiHeader> Headers { get; set; } = new()
    {
        [HeaderNames.ETag] = ConfigurationReadResponseHeaders.StringHeader(Definitions.Descriptions.ProjectSettingsEntityTag),
        [HeaderNames.CacheControl] = ConfigurationReadResponseHeaders.StringHeader(Definitions.Descriptions.CacheControlProjectSettings),
    };
}
