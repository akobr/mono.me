using ModelContextProtocol.Protocol;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Turns an SDK ApiException into an isError tool result the model can act on. The API's message and hint are copied
// only where they are safe: a 500 can carry a raw storage or vault message, and a 422 binding error can quote an
// evaluated secret. A stack trace (ErrorResponse.Error), Details and the raw response are never copied. The step-up is
// told by the SDK (StepUp.IsStepUpRequired), also from a body the client left as text; nothing of that text is copied.
public static class ApiErrors
{
    public const string BindingMessage = "A binding could not be evaluated.";
    public const string ServerMessage = "The Storyteller API failed. Try again later.";

    public const string UnauthorizedMessage =
        "The Storyteller API did not accept the token the MCP server got for the person from its token exchange. Sign in "
        + "to the MCP server again. If the person's Storyteller account was removed, register again with sform or the "
        + "admin UI; otherwise an administrator checks the person's account and the API's Auth:TokenExchange settings.";

    // The API's ErrorCode for what it refuses to the actor the token acts through (NoSecretReveal): a secret read, and
    // giving out credentials that read secrets (creating or resetting a machine with a Secrets scope, or replacing its
    // federation rules).
    public const string ActorRestrictedCode = "ActorRestricted";

    public const string ActorRestrictedMessage =
        "The Storyteller API refuses this to the assistant: it acts through the MCP server, whose actor may not reveal "
        + "secrets (NoSecretReveal). The API withholds secret values from it and gives out no credentials that read them "
        + "(creating or resetting a machine with a Secrets scope, replacing its federation rules). Do this with sform or "
        + "the admin UI.";

    // The API's ErrorCode for what needs a recent sign-in of the person (Auth:StepUp, RFC 9470): a 401 with the window in
    // Details.MaxAge and in the WWW-Authenticate challenge.
    public const string StepUpRequiredCode = Sdk.StepUp.ErrorCode;

    // The MCP client signs the person in at the identity provider itself and cannot ask it for a new sign-in (max_age); a
    // token exchange copies the old time of the sign-in.
    public const string StepUpRemedy =
        "A reconnect that reuses the person's browser session keeps the old sign-in: ask the person to sign out at the "
        + "identity provider first (for AuthKit, Sign out in the admin UI), then reconnect the MCP server in the client and "
        + "retry, or to make the change in the admin UI. The assistant cannot do this itself.";

    // A restore or a purge answers 200 and refuses each secret it may not touch for a stale sign-in with this code.
    public const string StepUpItemsNotice =
        "The items with ErrorCode StepUpRequired were refused because the person's sign-in is not recent: the Storyteller "
        + "API needs a recent sign-in of the person for secret values. " + StepUpRemedy;

    // The text of a step-up, with the window written out when the API names it ("within the last 10 minutes").
    public static string StepUpRequiredMessage(TimeSpan? maxAge)
    {
        var window = maxAge is { } value && value > TimeSpan.Zero ? $" (within the last {DescribeWindow(value)})" : string.Empty;

        return $"The Storyteller API needs a recent sign-in of the person for this{window}: reading or writing secret values, "
            + "creating or resetting machine credentials, changing federation rules. " + StepUpRemedy;
    }

    // "10 minutes", "1 hour", "90 seconds": the largest unit that divides the window, as the API writes it.
    public static string DescribeWindow(TimeSpan duration)
    {
        return Sdk.StepUp.DescribeWindow(duration);
    }

    public static CallToolResult ToResult(Sdk.ApiException exception)
    {
        var (errorCode, message, hint, errors) = Read(exception);
        var status = exception.StatusCode;
        long? maxAgeSeconds = null;

        // First: the API answers the actor's refusal of a read before the step-up, so a step-up is never an actor's.
        if (Sdk.StepUp.IsStepUpRequired(exception, out var maxAge))
        {
            errorCode = StepUpRequiredCode;
            message = StepUpRequiredMessage(maxAge);
            hint = null;
            errors = null;
            maxAgeSeconds = maxAge is { } window ? (long)Math.Floor(window.TotalSeconds) : null;
        }
        else if (string.Equals(errorCode, ActorRestrictedCode, StringComparison.Ordinal))
        {
            message = ActorRestrictedMessage;
            hint = null;
        }
        else if (status == 422)
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

        if (maxAgeSeconds is { } seconds)
        {
            details["MaxAge"] = seconds;
        }

        var text = $"Storyteller API answered {status}{(string.IsNullOrEmpty(errorCode) ? string.Empty : " " + errorCode)}: {message}"
            + (string.IsNullOrEmpty(hint) ? string.Empty : $" Hint: {hint}");

        return ToolResults.Error(text, details);
    }

    // The code for a log or a check: the step-up's known code, or the code of a body the client read.
    public static string? GetErrorCode(Sdk.ApiException exception)
    {
        return Sdk.StepUp.IsStepUpRequired(exception) ? StepUpRequiredCode : Read(exception).ErrorCode;
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
