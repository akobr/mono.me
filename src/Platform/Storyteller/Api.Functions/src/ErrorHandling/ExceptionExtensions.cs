using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Binding;

namespace _42.Platform.Storyteller.Api.ErrorHandling;

public static class ExceptionExtensions
{
    public static string TryGetErrorMessage(this Exception @this)
    {
        return @this.Message;
    }

    public static string? TryGetErrorHint(this Exception @this)
    {
        return !string.IsNullOrWhiteSpace(@this.HelpLink)
            ? $"For more information visit: {@this.HelpLink}"
            : null;
    }

    public static string? TryGetErrorCode(this Exception @this)
    {
        return @this switch
        {
            EvaluationLimitExceededException limit => $"binding.limit.{ToCodeName(limit.Kind)}",
            BindingEvaluationException => "binding.evaluation",
            _ => null,
        };
    }

    public static ErrorResponse ToErrorResponse(this Exception @this)
    {
        return new ErrorResponse
        {
            Error = @this,
            Message = @this.TryGetErrorMessage(),
            ErrorCode = @this.TryGetErrorCode(),
            Hint = @this.TryGetErrorHint(),
        };
    }

    private static string ToCodeName(EvaluationLimitKind kind)
    {
        var name = kind.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
