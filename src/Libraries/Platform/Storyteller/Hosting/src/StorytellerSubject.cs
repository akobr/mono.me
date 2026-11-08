using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A subject, sbt.{subject}: a customer or a tenant that uses responsibilities in its contexts. Its name is Name. The names
// of its relations are copied from an initializer, as the members of the annotation are.
public sealed class StorytellerSubject : StorytellerAnnotation
{
    private readonly IReadOnlyList<string> _contextNames = [];
    private readonly IReadOnlyList<string> _responsibilityNames = [];

    public StorytellerSubject()
    {
    }

    // contextNames, responsibilityNames: read-only already, as the mapper and WithDeletedAt hand them over.
    [SetsRequiredMembers]
    internal StorytellerSubject(StorytellerAnnotation annotation, IReadOnlyList<string> contextNames, IReadOnlyList<string> responsibilityNames)
        : base(annotation)
    {
        _contextNames = contextNames;
        _responsibilityNames = responsibilityNames;
    }

    // Its contexts, cnt.{subject}.{context}, by their context names, as of the read. Never null.
    public IReadOnlyList<string> ContextNames
    {
        get => _contextNames;
        init => _contextNames = CopyNames(value);
    }

    // The responsibilities it uses, through its usages usg.{subject}.{responsibility}, as of the read. Never null.
    public IReadOnlyList<string> ResponsibilityNames
    {
        get => _responsibilityNames;
        init => _responsibilityNames = CopyNames(value);
    }

    internal override StorytellerAnnotation WithDeletedAt(DateTimeOffset deletedAt)
    {
        return new StorytellerSubject(this, _contextNames, _responsibilityNames) { DeletedAt = deletedAt };
    }
}
