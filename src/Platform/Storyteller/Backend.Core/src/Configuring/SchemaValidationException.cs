namespace _42.Platform.Storyteller.Configuring;

public class SchemaValidationException : Exception
{
    public SchemaValidationException(IReadOnlyList<SchemaValidationError> validationErrors)
        : base("One or more existing configurations are not compliant with the provided schema.")
    {
        ValidationErrors = validationErrors ?? throw new ArgumentNullException(nameof(validationErrors));
    }

    public IReadOnlyList<SchemaValidationError> ValidationErrors { get; }
}

public class SchemaValidationError
{
    public required string AnnotationKey { get; init; }

    public required string ViewName { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }
}
