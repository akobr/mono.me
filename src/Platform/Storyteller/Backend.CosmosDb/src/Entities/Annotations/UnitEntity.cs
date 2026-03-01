namespace _42.Platform.Storyteller.Entities.Annotations;

public record UnitEntity : AnnotationEntity
{
    // Time, Message, File, Event, etc.
    public required string UnitType { get; init; }

    // Definition of time (CRON), message template, file template, etc.
    public required string UnitDefinition { get; init; }
}
