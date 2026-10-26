using _42.Platform.Storyteller.Mcp.Authentication;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The list-tools filter, registered only together with the secret tools: tools/list offers them only while the API
// lets the MCP server's actor read secrets. It asks the token exchange for the person (cached, so the calls that
// follow cost no second exchange) and removes the secret tools when the token carries NoSecretReveal. It fails closed:
// no token or a failed exchange hides them too, and never fails the list. The list then differs per person, so every
// answer is marked private for caches.
//
// Hiding is presentation only. StorytellerCallFilter refuses a hidden tool that is called anyway, and the API refuses
// the secret reads themselves.
public static class SecretToolsFilter
{
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Create(McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
    {
        return async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);
            result.CacheScope = CacheScope.Private;

            if (!result.Tools.Any(tool => SecretToolNames.Contains(tool.Name)) || await RevealsSecretsAsync(request, cancellationToken))
            {
                return result;
            }

            result.Tools = result.Tools.Where(tool => !SecretToolNames.Contains(tool.Name)).ToList();
            return result;
        };
    }

    // Logs carry the error code or the exception type only, as the call filter's do.
    private static async Task<bool> RevealsSecretsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        if (request.Services is not { } services)
        {
            return false;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(StorytellerCallFilter.LoggerCategory);

        try
        {
            var userToken = await StorytellerCallFilter.GetUserTokenAsync(services);

            if (string.IsNullOrEmpty(userToken))
            {
                return false;
            }

            var token = await services.GetRequiredService<IStorytellerTokenProvider>().GetTokenAsync(userToken, cancellationToken);
            return !token.WithholdsSecrets;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var reason = (exception as StorytellerSignInException)?.ErrorCode ?? exception.GetType().Name;
            logger.LogWarning("tools/list: no Storyteller token for the person ({Reason}); the secret tools are hidden.", reason);
            return false;
        }
    }
}
