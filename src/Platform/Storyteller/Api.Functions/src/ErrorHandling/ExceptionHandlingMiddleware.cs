using System.Net;
using _42.Platform.Storyteller.Api.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.ErrorHandling;

public class ExceptionHandlingMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (SecurityTokenException exception)
        {
            await ProcessSecurityExceptionAsync(context, exception);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error processing invocation");
            var httpReqData = await context.GetHttpRequestDataAsync();

            if (httpReqData is null)
            {
                return;
            }

            var httpErrorResponse = httpReqData.CreateResponse(HttpStatusCode.InternalServerError);

            // TODO: [P3] https://github.com/Azure/azure-functions-dotnet-worker/issues/776
            await httpErrorResponse.WriteAsJsonAsync(
                new ErrorResponse
                {
                    Error = exception,
                    Message = exception.TryGetErrorMessage(),
                    Hint = exception.TryGetErrorHint(),
                    ErrorCode = exception.TryGetErrorCode(),
                });

            context.SetInvocationResult(httpErrorResponse);
        }
    }

    private async Task ProcessSecurityExceptionAsync(FunctionContext context, Exception exception)
    {
        var httpReqData = await context.GetHttpRequestDataAsync();

        if (httpReqData is null)
        {
            return;
        }

        _logger.LogWarning(exception, "Authentication failed at: {url}", httpReqData.Url.AbsolutePath);
        var httpUnauthorizedResponse = httpReqData.CreateResponse(HttpStatusCode.Unauthorized);
        context.SetInvocationResult(httpUnauthorizedResponse);
    }
}
