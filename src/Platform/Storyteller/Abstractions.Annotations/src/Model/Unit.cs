namespace _42.Platform.Storyteller;

public record Unit : Annotation, IUnit
{
    // What a unit gets when a write does not name them: an automatic ancestor, an import, sform story set.
    public const string DefaultUnitType = "event";
    public const string DefaultUnitDefinition = "unknown";

    public required string ResponsibilityKey { get; init; }

    public required string ResponsibilityName { get; init; }

    // Time, Message, File, Event, etc.
    public required string UnitType { get; init; }

    // Definition of time (CRON), message template, file template, etc.
    public required string UnitDefinition { get; init; }
}
