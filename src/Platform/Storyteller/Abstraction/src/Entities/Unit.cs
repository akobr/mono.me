namespace _42.Platform.Storyteller.Entities;

public record Unit : Annotation
{
    // Time, Message, File, Event, etc.
    public string UnitType { get; init; }
}
