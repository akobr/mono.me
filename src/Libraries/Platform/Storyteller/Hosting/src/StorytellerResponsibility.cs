using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A responsibility, rst.{responsibility}: a capability that subjects use. Its name is Name. UnitNames is copied from an
// initializer, as the members of the annotation are.
public sealed class StorytellerResponsibility : StorytellerAnnotation
{
    private readonly IReadOnlyList<string> _unitNames = [];

    public StorytellerResponsibility()
    {
    }

    // unitNames: read-only already, as the mapper and WithDeletedAt hand them over.
    [SetsRequiredMembers]
    internal StorytellerResponsibility(StorytellerAnnotation annotation, IReadOnlyList<string> unitNames)
        : base(annotation)
    {
        _unitNames = unitNames;
    }

    // The units under it, unt.{responsibility}.{unit}, by their unit names, as of the read. Never null.
    public IReadOnlyList<string> UnitNames
    {
        get => _unitNames;
        init => _unitNames = CopyNames(value);
    }

    internal override StorytellerAnnotation WithDeletedAt(DateTimeOffset deletedAt)
    {
        return new StorytellerResponsibility(this, _unitNames) { DeletedAt = deletedAt };
    }
}
