using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A context, cnt.{subject}.{context}: an environment of a subject, such as production. The names come from the key, never
// from the body. ResponsibilityNames is copied from an initializer, as the members of the annotation are.
public sealed class StorytellerContext : StorytellerAnnotation
{
    private readonly IReadOnlyList<string> _responsibilityNames = [];

    public StorytellerContext()
    {
    }

    // responsibilityNames: read-only already, as the mapper and WithDeletedAt hand them over.
    [SetsRequiredMembers]
    internal StorytellerContext(StorytellerAnnotation annotation, string subjectName, string contextName, IReadOnlyList<string> responsibilityNames)
        : base(annotation)
    {
        SubjectName = subjectName;
        ContextName = contextName;
        _responsibilityNames = responsibilityNames;
    }

    public required string SubjectName { get; init; }

    public required string ContextName { get; init; }

    // The responsibilities that run in it, through its executions exe.{subject}.{responsibility}.{context}, as of the read.
    // Never null.
    public IReadOnlyList<string> ResponsibilityNames
    {
        get => _responsibilityNames;
        init => _responsibilityNames = CopyNames(value);
    }

    internal override StorytellerAnnotation WithDeletedAt(DateTimeOffset deletedAt)
    {
        return new StorytellerContext(this, SubjectName, ContextName, _responsibilityNames) { DeletedAt = deletedAt };
    }
}
