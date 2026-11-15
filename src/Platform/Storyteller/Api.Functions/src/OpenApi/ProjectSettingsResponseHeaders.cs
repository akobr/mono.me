using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

// The header of a write of project settings: the tag of the stored version.
public sealed class ProjectSettingsResponseHeaders : IOpenApiCustomResponseHeader
{
    public Dictionary<string, OpenApiHeader> Headers { get; set; } = new()
    {
        [HeaderNames.ETag] = ConfigurationReadResponseHeaders.StringHeader(Definitions.Descriptions.ProjectSettingsEntityTag),
    };
}
