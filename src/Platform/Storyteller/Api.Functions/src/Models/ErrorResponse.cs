namespace _42.Platform.Storyteller.Api.Models;

public class ErrorResponse
{
    public string Message { get; set; }

    public string? Hint { get; set; }

    public string? ErrorCode { get; set; }

    public Exception? Error { get; set; }
}
