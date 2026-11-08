using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A unit, unt.{responsibility}.{unit}: a part of a responsibility, such as a job, that an execution runs as a unit of
// execution. The names come from the key, never from the body.
public sealed class StorytellerUnit : StorytellerAnnotation
{
    public StorytellerUnit()
    {
    }

    [SetsRequiredMembers]
    internal StorytellerUnit(StorytellerAnnotation annotation, string responsibilityName, string unitName, string? unitType, string? unitDefinition)
        : base(annotation)
    {
        ResponsibilityName = responsibilityName;
        UnitName = unitName;
        UnitType = unitType;
        UnitDefinition = unitDefinition;
    }

    public required string ResponsibilityName { get; init; }

    public required string UnitName { get; init; }

    // What kind of unit it is, as the platform stores it; null when it has none.
    public string? UnitType { get; init; }

    // How to run it, as the platform stores it; null when it has none.
    public string? UnitDefinition { get; init; }

    internal override StorytellerAnnotation WithDeletedAt(DateTimeOffset deletedAt)
    {
        return new StorytellerUnit(this, ResponsibilityName, UnitName, UnitType, UnitDefinition) { DeletedAt = deletedAt };
    }
}
