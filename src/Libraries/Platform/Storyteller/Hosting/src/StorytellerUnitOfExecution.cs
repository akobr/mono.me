using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A unit of execution, uxe.{subject}.{responsibility}.{context}.{unit}: a unit that an execution runs, such as the job of
// one subject. The names come from the key, never from the body.
public sealed class StorytellerUnitOfExecution : StorytellerAnnotation
{
    public StorytellerUnitOfExecution()
    {
    }

    [SetsRequiredMembers]
    internal StorytellerUnitOfExecution(StorytellerAnnotation annotation, string subjectName, string responsibilityName, string contextName, string unitName)
        : base(annotation)
    {
        SubjectName = subjectName;
        ResponsibilityName = responsibilityName;
        ContextName = contextName;
        UnitName = unitName;
    }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    public required string ContextName { get; init; }

    public required string UnitName { get; init; }

    internal override StorytellerAnnotation WithDeletedAt(DateTimeOffset deletedAt)
    {
        return new StorytellerUnitOfExecution(this, SubjectName, ResponsibilityName, ContextName, UnitName) { DeletedAt = deletedAt };
    }
}
