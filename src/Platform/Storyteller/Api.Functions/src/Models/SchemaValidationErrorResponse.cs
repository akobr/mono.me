namespace _42.Platform.Storyteller.Api.Models;

public class SchemaValidationErrorResponse : ErrorResponse
{
    public SchemaValidationErrorResponse(string message, IReadOnlyList<SchemaValidationErrorDetail> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyList<SchemaValidationErrorDetail> Errors { get; init; }
}

public class SchemaValidationErrorDetail
{
    public required string AnnotationKey { get; init; }

    public required string ViewName { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }
}
