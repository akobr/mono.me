using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

// The header of the step-up 401 (RFC 9470): WWW-Authenticate with error="insufficient_user_authentication" and max_age.
public sealed class StepUpChallengeHeaders : IOpenApiCustomResponseHeader
{
    public Dictionary<string, OpenApiHeader> Headers { get; set; } = new()
    {
        [HeaderNames.WWWAuthenticate] = ConfigurationReadResponseHeaders.StringHeader(Definitions.Descriptions.StepUpChallenge),
    };
}
