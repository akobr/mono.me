using System.Globalization;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// GET /.well-known/jwks.json, anonymous: the public key of Mcp:Actor:ClientAssertionKey, which the API's ClientAssertion
// actor names as its JwksUri. 404 without a client assertion key, 503 while its file cannot be read, for RetryDelay after
// each failed read.
public static class ActorKeyEndpoints
{
    public const string PublicCacheControl = "public, max-age=300";

    // McpActorKey.RetryDelay, so a client that waits as long finds the next read.
    public static readonly string RetryAfterSeconds = ((int)McpActorKey.RetryDelay.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    public static IEndpointRouteBuilder MapActorKeys(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(McpActorKey.JwksPath, JsonWebKeySet).AllowAnonymous();
        return endpoints;
    }

    private static IResult JsonWebKeySet(McpActorKey key, HttpResponse response)
    {
        if (!key.IsConfigured)
        {
            return Results.NotFound();
        }

        try
        {
            var keys = key.GetJsonWebKeySet();
            response.Headers.CacheControl = PublicCacheControl;
            return Results.Json(keys);
        }
        catch (StorytellerActorException)
        {
            response.Headers.CacheControl = "no-store";
            response.Headers.RetryAfter = RetryAfterSeconds;
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
