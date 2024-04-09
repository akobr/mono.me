namespace _42.Platform.Storyteller;

public record Execution : Annotation, IExecution
{
    public required string ResponsibilityKey { get; init; }

    public required string SubjectKey { get; init; }

    public required string ContextKey { get; init; }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    public required string ContextName { get; init; }
}
