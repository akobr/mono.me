using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// An execution, exe.{subject}.{responsibility}.{context}: a subject that uses a responsibility in one of its contexts.
// The names come from the key, never from the body. UnitNames is copied from an initializer, as the members of the
// annotation are.
public sealed class StorytellerExecution : StorytellerAnnotation
{
    private readonly IReadOnlyList<string> _unitNames = [];

    public StorytellerExecution()
    {
    }

    // unitNames: read-only already, as the mapper and WithDeletedAt hand them over.
    [SetsRequiredMembers]
    internal StorytellerExecution(StorytellerAnnotation annotation, string subjectName, string responsibilityName, string contextName, IReadOnlyList<string> unitNames)
        : base(annotation)
    {
        SubjectName = subjectName;
        ResponsibilityName = responsibilityName;
        ContextName = contextName;
        _unitNames = unitNames;
    }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    public required string ContextName { get; init; }

    // The units of execution under it, uxe.{subject}.{responsibility}.{context}.{unit}, by their unit names. Never null.
    public IReadOnlyList<string> UnitNames
    {
        get => _unitNames;
        init => _unitNames = CopyNames(value);
    }

    internal override StorytellerAnnotation WithDeletedAt(DateTimeOffset deletedAt)
    {
        return new StorytellerExecution(this, SubjectName, ResponsibilityName, ContextName, _unitNames) { DeletedAt = deletedAt };
    }
}
