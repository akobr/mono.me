using _42.Platform.Storyteller.Mcp.Authentication;

using Microsoft.AspNetCore.Authentication;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Newtonsoft.Json.Linq;

using Polly.Timeout;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The one call-tool filter. Before a tool runs it gets the Storyteller token for the person (the API's token exchange)
// and puts it into the request's StorytellerAccessToken, where the SDK clients of the same scope read it; the inbound
// MCP token never reaches the API. A secret tool is refused while the API withholds secrets from the MCP server's actor
// (NoSecretReveal): SecretToolsFilter hides it from tools/list then, but a hidden tool can still be called. After the
// tool it turns API, sign-in and input errors into isError results, and so a call to the API that did not finish (a
// timeout, a broken connection).
//
// Logs carry the tool name, the status and the error code only: never an exception object of the SDK (its message and
// ToString hold the response body), an argument or a result.
public static class StorytellerCallFilter
{
    public const string LoggerCategory = "_42.Platform.Storyteller.Mcp.Tools";

    public const string UnfinishedCallMessage =
        "The call to the Storyteller API did not finish: it timed out or the connection broke. It may have completed on the "
        + "server, so check with the matching read (list_machines, get_annotation, get_configuration, get_key_value) before "
        + "repeating it. The one-time credentials of a completed create_machine or reset_machine cannot be read again: "
        + "reset the machine, or delete and recreate a Certificate machine.";

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return async (request, cancellationToken) =>
        {
            var services = request.Services ?? throw new InvalidOperationException("A tool call has no request services.");
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
            var tool = request.Params?.Name ?? string.Empty;

            try
            {
                var userToken = await GetUserTokenAsync(services);

                if (string.IsNullOrEmpty(userToken))
                {
                    return ToolResults.Error("The request carries no token of the signed-in person.");
                }

                StorytellerToken token;

                try
                {
                    token = await services.GetRequiredService<IStorytellerTokenProvider>().GetTokenAsync(userToken, cancellationToken);
                }
                catch (Exception exception) when (IsUnfinishedCall(exception, cancellationToken))
                {
                    // The exchange maps its own failures; one that still escapes it is a failed sign-in, never a tool error.
                    logger.LogWarning("Tool {Tool}: the token exchange did not finish ({ExceptionType}).", tool, exception.GetType().Name);
                    throw new StorytellerSignInUnavailableException();
                }

                if (token.WithholdsSecrets && SecretToolNames.Contains(tool))
                {
                    logger.LogWarning("Tool {Tool}: refused, the API withholds secrets from the MCP server's actor ({Restriction}).", tool, StorytellerToken.NoSecretReveal);
                    return ToolResults.Error(ApiErrors.ActorRestrictedMessage, new JObject { ["ErrorCode"] = ApiErrors.ActorRestrictedCode });
                }

                services.GetRequiredService<StorytellerAccessToken>().Value = token.AccessToken;

                // Only the tool's own calls: a failed token exchange above is a sign-in error, not an unfinished write.
                try
                {
                    return await next(request, cancellationToken);
                }
                catch (Exception exception) when (IsUnfinishedCall(exception, cancellationToken))
                {
                    logger.LogWarning("Tool {Tool}: a call to the Storyteller API did not finish ({ExceptionType}).", tool, exception.GetType().Name);
                    return ToolResults.Error(UnfinishedCallMessage);
                }
            }
            catch (Sdk.ApiException exception)
            {
                logger.LogWarning("Tool {Tool}: the Storyteller API answered {Status} {ErrorCode}.", tool, exception.StatusCode, ApiErrors.GetErrorCode(exception));
                return ApiErrors.ToResult(exception);
            }
            catch (StorytellerSignInException exception)
            {
                logger.LogWarning("Tool {Tool}: no Storyteller token for the person ({ErrorCode}).", tool, exception.ErrorCode);
                return ToolResults.Error(exception.Message);
            }
            catch (ToolInputException exception)
            {
                return ToolResults.Error(exception.Message);
            }
        };
    }

    // The person's MCP token, which the bearer handler saved (SaveToken); null without one.
    internal static async Task<string?> GetUserTokenAsync(IServiceProvider services)
    {
        var httpContext = services.GetRequiredService<IHttpContextAccessor>().HttpContext
            ?? throw new InvalidOperationException("An MCP request runs outside an HTTP request.");
        return await httpContext.GetTokenAsync("access_token");
    }

    // The resilience handler's timeout, a transport failure, or a cancellation the caller did not ask for (HttpClient's
    // own timeout). The caller's cancellation stays a cancellation.
    internal static bool IsUnfinishedCall(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            TimeoutRejectedException or HttpRequestException => true,
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false,
        };
    }
}
