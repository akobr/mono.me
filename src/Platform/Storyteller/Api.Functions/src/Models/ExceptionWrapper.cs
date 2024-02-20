namespace _42.Platform.Storyteller.Api.Models;

public class ExceptionWrapper
{
    public ExceptionWrapper(Exception exception)
    {
        HResult = exception.HResult;
        Message = exception.Message;
        Source = exception.Source;
        StackTrace = exception.StackTrace;
        HelpLink = exception.HelpLink;

        if (exception.InnerException is not null)
        {
            InnerException = new ExceptionWrapper(exception.InnerException);
        }
    }

    public string Message { get; }

    public string? Source { get; }

    public string? StackTrace { get; }

    public string? HelpLink { get; }

    public int HResult { get; }

    public ExceptionWrapper? InnerException { get; }

    public static implicit operator ExceptionWrapper(Exception exception)
    {
        return new ExceptionWrapper(exception);
    }
}
