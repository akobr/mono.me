using System.Security.Cryptography;

using _42.Platform.Storyteller.Api.Security;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using HttpRequestData = Microsoft.Azure.Functions.Worker.Http.HttpRequestData;

namespace _42.Platform.Storyteller.Api.ConditionalReads;

// Conditional GET (RFC 9110 §13.1.2): the entity tag goes out with the 200 and the 304, and an If-None-Match
// that lists it (weak comparison) or is * is answered with 304 Not Modified and no body.
public static class ConditionalRequestExtensions
{
    // Keys the tags over resolved bindings when ConditionalReads:EntityTagKey is not set; correct, but per process.
    private static readonly Lazy<byte[]> ProcessEntityTagKey = new(() => RandomNumberGenerator.GetBytes(ConditionalReadOptions.MinimumKeyBytes));

    public static IActionResult OkOrNotModified(this HttpRequestData @this, object value, EntityTagHeaderValue entityTag)
    {
        @this.SetResponseHeader(HeaderNames.ETag, entityTag.ToString());

        return @this.MatchesIfNoneMatch(entityTag)
            ? new StatusCodeResult(StatusCodes.Status304NotModified)
            : new OkObjectResult(value);
    }

    // Every If-None-Match line and list item counts. A header that is not a strict list of entity tags is ignored.
    public static bool MatchesIfNoneMatch(this HttpRequestData @this, EntityTagHeaderValue entityTag)
    {
        // Compare never matches * with a tag, so it is checked first
        return @this.GetIfNoneMatch().Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(entityTag, useStrongComparison: false));
    }

    // The tags listed in If-None-Match, in order; empty without the header or when it is not a strict list of entity tags.
    public static IReadOnlyList<EntityTagHeaderValue> GetIfNoneMatch(this HttpRequestData @this)
    {
        return @this.Headers.TryGetValues(HeaderNames.IfNoneMatch, out var values)
               && EntityTagHeaderValue.TryParseStrictList(values.ToList(), out var listed)
            ? listed.ToList()
            : [];
    }

    // The manifests of the resolved reads of this process, or null without them (nothing is cached then).
    public static DependencyManifests? GetDependencyManifests(this HttpRequestData @this)
    {
        return @this.FunctionContext.InstanceServices.GetService<DependencyManifests>();
    }

    // Shared caches never store the response, and a browser revalidates its copy before each use.
    public static void MarkNoCache(this HttpRequestData @this)
    {
        @this.SetResponseHeader(HeaderNames.CacheControl, "private, no-cache");
    }

    // The configured key of the tags over resolved bindings, or the key of this process.
    public static byte[] GetEntityTagKey(this HttpRequestData @this)
    {
        var options = @this.FunctionContext.InstanceServices.GetService<IOptions<ConditionalReadOptions>>()?.Value;
        return options?.GetEntityTagKey() ?? ProcessEntityTagKey.Value;
    }
}
