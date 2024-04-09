namespace _42.Platform.Storyteller.Entities.Annotations;

public record class ContextEntity : AnnotationEntity
{
    public required string SubjectKey { get; init; }

    public required string SubjectName { get; init; }
}
