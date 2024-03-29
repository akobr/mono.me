namespace _42.Platform.Storyteller.Entities;

public record class Execution : Annotation
{
    public required string ResponsibilityKey { get; init; }

    public required string SubjectKey { get; init; }

    public required string ContextKey { get; init; }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    public required string ContextName { get; init; }
}
