using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

// The headers of the resolved read, on its 200 and 304 responses: Cache-Control follows the secret rights of the caller,
// and X-2S-Secrets-Withheld says when the mTLS policy kept the secrets back.
public sealed class ResolvedConfigurationReadResponseHeaders : IOpenApiCustomResponseHeader
{
    public Dictionary<string, OpenApiHeader> Headers { get; set; } = new()
    {
        [HeaderNames.ETag] = ConfigurationReadResponseHeaders.StringHeader(Definitions.Descriptions.EntityTag),
        [HeaderNames.CacheControl] = ConfigurationReadResponseHeaders.StringHeader(Definitions.Descriptions.CacheControlResolvedRead),
        [SecretRights.WithheldHeader] = ConfigurationReadResponseHeaders.StringHeader(Definitions.Descriptions.SecretsWithheld),
    };
}
