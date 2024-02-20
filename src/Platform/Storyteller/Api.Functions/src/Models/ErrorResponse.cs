namespace _42.Platform.Storyteller.Api.Models;

public class ErrorResponse
{
    public ErrorResponse()
    {
        Message = string.Empty;
    }

    public ErrorResponse(string message)
    {
        Message = message;
    }

    public string Message { get; init; }

    public string? Hint { get; set; }

    public string? ErrorCode { get; set; }

    public ExceptionWrapper? Error { get; set; }
}
