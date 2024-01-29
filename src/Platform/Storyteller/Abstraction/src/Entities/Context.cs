namespace _42.Platform.Storyteller.Entities;

public record class Context : Annotation
{
    public required string SubjectKey { get; init; }

    public required string SubjectName { get; init; }
}
