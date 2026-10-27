using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// An execution, exe.{subject}.{responsibility}.{context}: a subject that uses a responsibility in one of its contexts.
// The names come from the key, never from the body.
public sealed class StorytellerExecution : StorytellerAnnotation
{
    public StorytellerExecution()
    {
    }

    [SetsRequiredMembers]
    internal StorytellerExecution(StorytellerAnnotation annotation, string subjectName, string responsibilityName, string contextName, IReadOnlyList<string> unitNames)
        : base(annotation)
    {
        SubjectName = subjectName;
        ResponsibilityName = responsibilityName;
        ContextName = contextName;
        UnitNames = unitNames;
    }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    public required string ContextName { get; init; }

    // The units of execution under it, uxe.{subject}.{responsibility}.{context}.{unit}, by their unit names.
    public IReadOnlyList<string> UnitNames { get; internal init; } = [];

    internal override StorytellerAnnotation WithDeletedAt(DateTimeOffset deletedAt)
    {
        return new StorytellerExecution(this, SubjectName, ResponsibilityName, ContextName, UnitNames) { DeletedAt = deletedAt };
    }
}
