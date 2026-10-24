using ModelContextProtocol.Protocol;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Turns an SDK ApiException into an isError tool result the model can act on. The API's message and hint are copied
// only where they are safe: a 500 can carry a raw storage or vault message, and a 422 binding error can quote an
// evaluated secret. A stack trace (ErrorResponse.Error), Details and the raw response are never copied.
public static class ApiErrors
{
    public const string BindingMessage = "A binding could not be evaluated.";
    public const string ServerMessage = "The Storyteller API failed. Try again later.";

    public const string UnauthorizedMessage =
        "The Storyteller API did not accept the token of the MCP server. Check that the API lists the MCP app and "
        + "tenant in Auth:DelegatedClients and that the token carries User.Impersonation.";

    public static CallToolResult ToResult(Sdk.ApiException exception)
    {
        var (errorCode, message, hint, errors) = Read(exception);
        var status = exception.StatusCode;

        if (status == 422)
        {
            message = BindingMessage;
            hint = null;
            errors = null;
        }
        else if (status >= 500)
        {
            message = ServerMessage;
            hint = null;
            errors = null;
        }
        else if (string.IsNullOrWhiteSpace(message))
        {
            message = DefaultMessage(status);
        }

        var details = new JObject { ["Status"] = status };

        if (!string.IsNullOrEmpty(errorCode))
        {
            details["ErrorCode"] = errorCode;
        }

        // Structured Message is the API's (or the fixed) message alone; the text below composes it with status and hint.
        details["Message"] = message;

        if (!string.IsNullOrEmpty(hint))
        {
            details["Hint"] = hint;
        }

        if (errors is not null)
        {
            details["Errors"] = errors;
        }

        var text = $"Storyteller API answered {status}{(string.IsNullOrEmpty(errorCode) ? string.Empty : " " + errorCode)}: {message}"
            + (string.IsNullOrEmpty(hint) ? string.Empty : $" Hint: {hint}");

        return ToolResults.Error(text, details);
    }

    public static string? GetErrorCode(Sdk.ApiException exception)
    {
        return Read(exception).ErrorCode;
    }

    private static (string? ErrorCode, string? Message, string? Hint, JArray? Errors) Read(Sdk.ApiException exception)
    {
        return exception switch
        {
            Sdk.ApiException<Sdk.SchemaValidationErrorResponse> { Result: { } result } => (
                result.ErrorCode,
                result.Message,
                result.Hint,
                result.Errors is null ? null : new JArray(result.Errors.Select(error => new JObject
                {
                    ["AnnotationKey"] = error.AnnotationKey,
                    ["ViewName"] = error.ViewName,
                    ["Errors"] = new JArray(error.Errors ?? []),
                }))),
            Sdk.ApiException<Sdk.ErrorResponse> { Result: { } result } => (result.ErrorCode, result.Message, result.Hint, null),
            _ => (null, null, null, null),
        };
    }

    private static string DefaultMessage(int status)
    {
        return status switch
        {
            400 => "The API refused the request as invalid.",
            401 => UnauthorizedMessage,
            403 => "The signed-in person has no access to this operation; a higher role on the project is needed.",
            404 => "It does not exist, or it is not visible to the signed-in person.",
            409 => "It conflicts with the current state; read it again and retry.",
            412 => "A precondition failed: it changed since it was read.",
            413 => "The request is too large.",
            _ => "The API refused the request.",
        };
    }
}
