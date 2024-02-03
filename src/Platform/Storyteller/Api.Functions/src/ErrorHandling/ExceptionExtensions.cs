using _42.Platform.Storyteller.Api.Models;

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
        return null;
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
}
