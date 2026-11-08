using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A usage, usg.{subject}.{responsibility}: a subject may use a responsibility, on the terms of its labels and values. The
// names come from the key, never from the body. ContextNames is copied from an initializer, as the members of the
// annotation are.
public sealed class StorytellerUsage : StorytellerAnnotation
{
    private readonly IReadOnlyList<string> _contextNames = [];

    public StorytellerUsage()
    {
    }

    // contextNames: read-only already, as the mapper and WithDeletedAt hand them over.
    [SetsRequiredMembers]
    internal StorytellerUsage(StorytellerAnnotation annotation, string subjectName, string responsibilityName, IReadOnlyList<string> contextNames)
        : base(annotation)
    {
        SubjectName = subjectName;
        ResponsibilityName = responsibilityName;
        _contextNames = contextNames;
    }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    // The contexts it runs in, through its executions exe.{subject}.{responsibility}.{context}, as of the read. Never null.
    public IReadOnlyList<string> ContextNames
    {
        get => _contextNames;
        init => _contextNames = CopyNames(value);
    }

    internal override StorytellerAnnotation WithDeletedAt(DateTimeOffset deletedAt)
    {
        return new StorytellerUsage(this, SubjectName, ResponsibilityName, _contextNames) { DeletedAt = deletedAt };
    }
}
