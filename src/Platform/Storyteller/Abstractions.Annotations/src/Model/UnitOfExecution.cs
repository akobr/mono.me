namespace _42.Platform.Storyteller;

public record UnitOfExecution : Annotation, IUnitOfExecution
{
    public required string ResponsibilityKey { get; init; }

    public required string UnitKey { get; init; }

    public required string SubjectKey { get; init; }

    public required string ContextKey { get; init; }

    public required string ResponsibilityName { get; init; }

    public required string UnitName { get; init; }

    public required string SubjectName { get; init; }

    public required string ContextName { get; init; }
}
