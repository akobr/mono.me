using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;

namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// A target of the catalog in the organization, project and view it reads in: the ancestor's full key, the descendants'
// type, the filters, and what the target asks of the descendants' status and of their ancestors'. What the catalog's
// lists and the descendant informer read.
internal sealed class DescendantScope
{
    private readonly Func<AnnotationKey, bool> _passes;
    private readonly Func<string, string> _describe;

    private DescendantScope(FullKey ancestor, AnnotationType type, bool includeInactive, bool requireActiveAncestors, Func<AnnotationKey, bool> passes, Func<string, string> describe)
    {
        Ancestor = ancestor;
        Type = type;
        IncludeInactive = includeInactive;
        RequireActiveAncestors = requireActiveAncestors;
        _passes = passes;
        _describe = describe;
    }

    // With the type code lower-cased.
    public FullKey Ancestor { get; }

    public AnnotationType Type { get; }

    public bool IncludeInactive { get; }

    // Only the descendants whose ancestors are all active.
    public bool RequireActiveAncestors { get; }

    // What the messages of a list name, "the units of {ancestor}".
    public string ListText => $"the {AnnotationTypeFacts.Plural(Type)} of {Ancestor}";

    // view: any full key of the view, as the options' Key; an annotation key of the target needs it, and throws
    // StorytellerConfigurationException without it, as who am I does without a key.
    public static DescendantScope Create<TAnnotation>(StorytellerTarget<TAnnotation> target, FullKey? view)
        where TAnnotation : StorytellerAnnotation
    {
        ArgumentNullException.ThrowIfNull(target);

        var ancestor = target.FullAncestorKey ?? CatalogKeys.Parse(target.Of, view, nameof(target));
        return new DescendantScope(ancestor, target.Type, target.IncludeInactive, target.RequireActiveAncestors, target.Passes, target.Describe);
    }

    // An item of the list, by its key: of the type and under the ancestor, as the platform lists them. A key that is
    // not is no item of this list.
    public bool IsListed(AnnotationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key.Type == Type && key.IsDescendantOf(Ancestor.Annotation);
    }

    // A listed key with the names of the filters.
    public bool Passes(AnnotationKey key)
    {
        return _passes(key);
    }

    // The full key of a listed one, with the type code lower-cased.
    public FullKey KeyOf(AnnotationKey key)
    {
        return CatalogKeys.Normalize(FullKey.Create(key, Ancestor));
    }

    // An annotation that is a descendant: not deleted, and active unless inactive ones are included.
    public bool Accepts(StorytellerAnnotation annotation, DateTimeOffset now)
    {
        return annotation.DeletedAt is null
            && (IncludeInactive || annotation.GetStatus(now) == AnnotationStatus.Active);
    }

    // The descendants of an ancestor that does not exist are none, which would read as an empty list for ever.
    public StorytellerConfigurationException DoesNotExist(StorytellerConfigurationException inner)
    {
        var key = Ancestor.ToString();
        return new StorytellerConfigurationException(
            $"The annotation {key} does not exist, so it has no {AnnotationTypeFacts.Plural(Type)} to list. Create it, or target an annotation that exists.",
            key,
            404,
            null,
            false,
            inner);
    }

    // Whether an ancestor that the machine may not read is active cannot be told, so neither can which descendants are
    // under active ancestors. ancestor: of a descendant. inner: the 403 AnnotationRestricted of its read.
    public StorytellerConfigurationException CannotReadAncestor(FullKey ancestor, StorytellerConfigurationException inner)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        ArgumentNullException.ThrowIfNull(inner);

        var key = ancestor.ToString();
        return new StorytellerConfigurationException(
            $"The machine may not read {key}, so the catalog cannot tell which of {ListText} are under active ancestors. Requiring active ancestors needs a machine that may read them: an unrestricted one for the executions of a responsibility, whose subjects and contexts are not under it.",
            key,
            inner.StatusCode,
            inner.ErrorCode,
            false,
            inner);
    }

    // "the units of @acme.billing.default.rst.invoicing of the unit daily".
    public override string ToString()
    {
        return _describe(Ancestor.ToString());
    }
}
