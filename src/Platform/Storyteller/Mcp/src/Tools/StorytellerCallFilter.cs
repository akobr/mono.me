using _42.Platform.Storyteller.Mcp.Authentication;

using Microsoft.AspNetCore.Authentication;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The one call-tool filter. Before a tool runs it gets the Storyteller token for the person (on-behalf-of) and puts it
// into the request's StorytellerAccessToken, where the SDK clients of the same scope read it; the inbound MCP token never
// reaches the API. After the tool it turns API, sign-in and input errors into isError results.
//
// Logs carry the tool name, the status and the error code only: never an exception object of the SDK (its message and
// ToString hold the response body), an argument or a result.
public static class StorytellerCallFilter
{
    public const string LoggerCategory = "_42.Platform.Storyteller.Mcp.Tools";

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return async (request, cancellationToken) =>
        {
            var services = request.Services ?? throw new InvalidOperationException("A tool call has no request services.");
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
            var tool = request.Params?.Name ?? string.Empty;

            try
            {
                var httpContext = services.GetRequiredService<IHttpContextAccessor>().HttpContext
                    ?? throw new InvalidOperationException("A tool call runs outside an HTTP request.");
                var userToken = await httpContext.GetTokenAsync("access_token");

                if (string.IsNullOrEmpty(userToken))
                {
                    return ToolResults.Error("The request carries no token of the signed-in person.");
                }

                var token = await services.GetRequiredService<IStorytellerTokenProvider>().GetTokenAsync(userToken, cancellationToken);
                services.GetRequiredService<StorytellerAccessToken>().Value = token;

                return await next(request, cancellationToken);
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
}
