namespace _42.Platform.Storyteller;

public record Unit : Annotation, IUnit
{
    public required string ResponsibilityKey { get; init; }

    public required string ResponsibilityName { get; init; }
}
