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
// (NoSecretReveal), and a tool outside ReadOnlyToolSet while the API lets the person's client app only read (ReadOnly):
// ToolListFilter hides them from tools/list then, but a hidden tool can still be called. After the
// tool it turns API, sign-in and input errors into isError results, and so a call to the API that did not finish (a
// timeout, a broken connection).
//
// A tool call has a deadline (ToolCallTimeout) for the exchange and every call of the API together: it ends with this
// filter's own answer while the client still waits for one. The tools pass their token to every SDK call, so the
// deadline stops the call that is running.
//
// Logs carry the tool name, the status and the error code only: never an exception object of the SDK (its message and
// ToString hold the response body), an argument or a result.
public static class StorytellerCallFilter
{
    public const string LoggerCategory = "_42.Platform.Storyteller.Mcp.Tools";

    public const string UnfinishedCallMessage =
        "The call to the Storyteller API did not finish: it timed out or the connection broke. It may have completed on the "
        + "server, so check with the matching read (list_machines, get_annotation, get_configuration, get_key_value) before "
        + "repeating it. " + LostCredentials;

    // The longest a tool call may take: the token exchange and every call of the API. In production the Container Apps
    // ingress drops a request after 240 seconds in which nothing was sent (a fixed idle timeout of the Consumption
    // profile), the start of a replica after scale to zero included, and the API's front end answers 502 after 230
    // seconds while the function goes on. 40 seconds stay for the start of a replica, the authentication and the result.
    public static readonly TimeSpan ToolCallTimeout = TimeSpan.FromSeconds(200);

    public static readonly string ToolCallTimeoutMessage =
        $"The tool call was stopped after {ToolCallTimeout.TotalSeconds:0} seconds, the longest the MCP server lets a tool "
        + "call run. A call to the Storyteller API that was still running may have completed on the server, so check with "
        + "the matching read (list_machines, get_annotation, get_configuration, get_key_value) before repeating a write; "
        + "repeat a read with a narrower request (a smaller limit, fewer keys). " + LostCredentials;

    private const string LostCredentials =
        "The one-time credentials of a completed create_machine or reset_machine cannot be read again: reset the machine, "
        + "or delete and recreate a Certificate machine.";

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return async (request, cancellationToken) =>
        {
            var services = request.Services ?? throw new InvalidOperationException("A tool call has no request services.");
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
            var tool = request.Params?.Name ?? string.Empty;

            // The caller's own cancellation stays a cancellation: the filters below test cancellationToken, not call.
            using var deadline = new CancellationTokenSource(ToolCallTimeout, services.GetService<TimeProvider>() ?? TimeProvider.System);
            using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

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
                    token = await services.GetRequiredService<IStorytellerTokenProvider>().GetTokenAsync(userToken, call.Token);
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

                // By the tool the call matched, so a call that matched none is refused too.
                if (token.IsReadOnly && !ReadOnlyToolSet.Contains(request.MatchedPrimitive))
                {
                    logger.LogWarning("Tool {Tool}: refused, the API lets the person's client app only read ({Restriction}).", tool, StorytellerToken.ReadOnly);
                    return ToolResults.Error(ApiErrors.ActorReadOnlyMessage, new JObject { ["ErrorCode"] = ApiErrors.ActorReadOnlyCode });
                }

                services.GetRequiredService<StorytellerAccessToken>().Value = token.AccessToken;

                // Only the tool's own calls: a failed token exchange above is a sign-in error, not an unfinished write.
                try
                {
                    return await next(request, call.Token);
                }
                catch (Exception exception) when (IsUnfinishedCall(exception, cancellationToken))
                {
                    if (deadline.IsCancellationRequested)
                    {
                        logger.LogWarning(
                            "Tool {Tool}: stopped at the deadline of a tool call, {Seconds} seconds ({ExceptionType}).",
                            tool,
                            ToolCallTimeout.TotalSeconds,
                            exception.GetType().Name);
                        return ToolResults.Error(ToolCallTimeoutMessage);
                    }

                    logger.LogWarning(
                        "Tool {Tool}: a call to the Storyteller API did not finish ({ExceptionType}).",
                        tool,
                        (FindBrokenBody(exception) ?? exception).GetType().Name);
                    return ToolResults.Error(UnfinishedCallMessage);
                }
                catch (Sdk.ApiException exception) when (exception.StatusCode == 401 && !Sdk.StepUp.IsStepUpRequired(exception, out _))
                {
                    // The API no longer accepts the cached token: the client app was blocked, the actor removed, or the
                    // exchange turned off. The next call exchanges again, and the exchange names the reason. Not for a
                    // step-up, which no new exchange lifts (it copies the same sign-in time) and which would only spend
                    // the API's replay limit per subject token.
                    services.GetRequiredService<IStorytellerTokenProvider>().Forget(userToken, token.AccessToken);
                    logger.LogWarning("Tool {Tool}: the Storyteller API answered {Status} {ErrorCode}.", tool, exception.StatusCode, ApiErrors.GetErrorCode(exception));
                    return ApiErrors.ToResult(exception);
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

    // The resilience handler's timeout, a transport failure (a response body that breaks while it is read included), or
    // a cancellation the caller did not ask for (the deadline of the tool call, HttpClient's own timeout). The caller's
    // cancellation stays a cancellation.
    internal static bool IsUnfinishedCall(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            TimeoutRejectedException or HttpRequestException or HttpIOException => true,
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            Sdk.ApiException => FindBrokenBody(exception) is not null,
            _ => false,
        };
    }

    // The SDK reports a body it could not read as an ApiException with the status of the answer, the one-time credentials
    // of a new machine in a 200 among them; Newtonsoft can wrap the transport failure in a JsonSerializationException.
    private static HttpIOException? FindBrokenBody(Exception exception)
    {
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is HttpIOException broken)
            {
                return broken;
            }
        }

        return null;
    }
}
