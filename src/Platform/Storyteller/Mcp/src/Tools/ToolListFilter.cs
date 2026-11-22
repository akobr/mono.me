using _42.Platform.Storyteller.Mcp.Authentication;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The list-tools filter, always registered: tools/list follows the restrictions the API puts on the MCP server's actor
// for the person's client app. It asks the token exchange for the person once per list (cached, so the calls that follow
// cost no second exchange) and
// - hides the secret tools (SecretToolNames) when the token carries NoSecretReveal, and when no token could be had: that
//   part fails closed;
// - hides every tool outside ReadOnlyToolSet when the token carries ReadOnly: that part fails open. With no token, a
//   failed exchange, or one that takes longer than TokenWait (a cold start of the API), the list keeps those tools, so a
//   session never starts with a read-only list by accident. Only the wait is cut: the shared exchange goes on and is
//   cached for the next call.
// It never fails the list. The list then differs per person, so every answer is marked private for caches.
//
// Hiding is presentation only. StorytellerCallFilter refuses a hidden tool that is called anyway, and the API refuses the
// calls themselves.
public static class ToolListFilter
{
    // How long tools/list waits for the person's token before it lists the tools as if no token could be had.
    public static readonly TimeSpan TokenWait = TimeSpan.FromSeconds(5);

    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Create(McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
    {
        return Create(next, TokenWait);
    }

    // Tests give a shorter wait.
    internal static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Create(McpRequestHandler<ListToolsRequestParams, ListToolsResult> next, TimeSpan tokenWait)
    {
        return async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);
            result.CacheScope = CacheScope.Private;

            var token = await GetTokenAsync(request, tokenWait, cancellationToken);
            var revealsSecrets = token is { WithholdsSecrets: false };
            var readOnly = token is { IsReadOnly: true };

            result.Tools = result.Tools
                .Where(tool => revealsSecrets || !SecretToolNames.Contains(tool.Name))
                .Where(tool => !readOnly || ReadOnlyToolSet.Contains(tool.Name, tool.Annotations?.ReadOnlyHint))
                .ToList();
            return result;
        };
    }

    // Null when no token could be had. Logs carry the error code or the exception type only, as the call filter's do.
    private static async Task<StorytellerToken?> GetTokenAsync(RequestContext<ListToolsRequestParams> request, TimeSpan tokenWait, CancellationToken cancellationToken)
    {
        if (request.Services is not { } services)
        {
            return null;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(StorytellerCallFilter.LoggerCategory);

        try
        {
            var userToken = await StorytellerCallFilter.GetUserTokenAsync(services);

            if (string.IsNullOrEmpty(userToken))
            {
                return null;
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(tokenWait);

            try
            {
                return await services.GetRequiredService<IStorytellerTokenProvider>().GetTokenAsync(userToken, wait.Token);
            }
            catch (OperationCanceledException) when (wait.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "tools/list: the Storyteller token for the person took longer than {Seconds} seconds; the secret tools are hidden and the other tools are listed.",
                    tokenWait.TotalSeconds);
                return null;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var reason = (exception as StorytellerSignInException)?.ErrorCode ?? exception.GetType().Name;
            logger.LogWarning("tools/list: no Storyteller token for the person ({Reason}); the secret tools are hidden.", reason);
            return null;
        }
    }
}
