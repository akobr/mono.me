namespace _42.Platform.Storyteller;

public record Unit : Annotation, IUnit
{
    public required string ResponsibilityKey { get; init; }

    public required string ResponsibilityName { get; init; }

    // Time, Message, File, Event, etc.
    public required string UnitType { get; init; }

    // Definition of time (CRON), message template, file template, etc.
    public required string UnitDefinition { get; init; }
}
