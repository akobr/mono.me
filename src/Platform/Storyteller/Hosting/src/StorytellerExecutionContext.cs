using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// Who am I: the annotation of the key and those of its ancestors that were read, as of ReadAt. A key of any type: an
// execution (a service of one subject), a unit of execution (a job), or a responsibility (a worker that discovers its
// subjects through the catalog). Immutable: Ancestors and Unreadable are copied from an initializer, so an application
// can build one for a test double of who am I and change its own collections afterwards.
public sealed class StorytellerExecutionContext
{
    private readonly IReadOnlyDictionary<AnnotationType, StorytellerAnnotation> _ancestors = ReadOnlyDictionary<AnnotationType, StorytellerAnnotation>.Empty;
    private readonly IReadOnlyList<string> _unreadable = [];

    public StorytellerExecutionContext()
    {
    }

    // A copy that differs only in what its initializer sets.
    [SetsRequiredMembers]
    private StorytellerExecutionContext(StorytellerExecutionContext source)
    {
        Key = source.Key;
        Annotation = source.Annotation;
        _ancestors = source._ancestors;
        _unreadable = source._unreadable;
        ReadAt = source.ReadAt;
    }

    // The full key, with the type code lower-cased.
    public required string Key { get; init; }

    // Of the key; a StorytellerExecution for an exe key.
    public required StorytellerAnnotation Annotation { get; init; }

    // The ancestors that were read, by their type. One that the credential may not read is in Unreadable; one deleted
    // after the key was read is in neither. Never null.
    public IReadOnlyDictionary<AnnotationType, StorytellerAnnotation> Ancestors
    {
        get => _ancestors;
        init => _ancestors = new Dictionary<AnnotationType, StorytellerAnnotation>(value ?? ReadOnlyDictionary<AnnotationType, StorytellerAnnotation>.Empty).AsReadOnly();
    }

    // The full keys of the ancestors the credential may not read (AnnotationRestricted): a machine restricted to its
    // annotation reads that annotation and its descendants only. Never null.
    public IReadOnlyList<string> Unreadable
    {
        get => _unreadable;
        init => _unreadable = StorytellerAnnotation.CopyNames(value);
    }

    public required DateTimeOffset ReadAt { get; init; }

    // The annotation of each type: the key's own, or an ancestor.
    public StorytellerAnnotation? Subject => Find(AnnotationType.Subject);

    public StorytellerAnnotation? Responsibility => Find(AnnotationType.Responsibility);

    public StorytellerAnnotation? Usage => Find(AnnotationType.Usage);

    public StorytellerAnnotation? Context => Find(AnnotationType.Context);

    // The key's own for an exe key, its parent for a uxe key.
    public StorytellerExecution? Execution => Find(AnnotationType.Execution) as StorytellerExecution;

    // Is it active: Deleted when the key's annotation or its execution is deleted; else the first status that is not
    // active among the key's annotation, its execution and its usage; Unknown when all that were read are active but
    // the usage (or the execution of a unit of execution) was unreadable; else Active. A key without an execution in its
    // chain (rst, sbt, unt, usg, cnt) has the status of its own annotation.
    public StorytellerSubscriptionStatus GetSubscriptionStatus(DateTimeOffset now)
    {
        var execution = Execution;

        if (Annotation.DeletedAt is not null || execution?.DeletedAt is not null)
        {
            return StorytellerSubscriptionStatus.Deleted;
        }

        if (Annotation.Type is not (AnnotationType.Execution or AnnotationType.UnitOfExecution))
        {
            return ToSubscriptionStatus(Annotation.GetStatus(now));
        }

        foreach (var annotation in new[] { Annotation, execution, Usage })
        {
            if (annotation?.GetStatus(now) is { } status and not AnnotationStatus.Active)
            {
                return ToSubscriptionStatus(status);
            }
        }

        return IsUnreadable(AnnotationType.Usage) || IsUnreadable(AnnotationType.Execution)
            ? StorytellerSubscriptionStatus.Unknown
            : StorytellerSubscriptionStatus.Active;
    }

    public override string ToString()
    {
        return $"{Key}, read at {ReadAt:O}";
    }

    // The same context, its key gone at a time: a 404 of a background read. A key that was deleted keeps the time of its
    // deletion.
    internal StorytellerExecutionContext WithKeyGoneAt(DateTimeOffset at)
    {
        return new StorytellerExecutionContext(this)
        {
            Annotation = Annotation.DeletedAt is null ? Annotation.WithDeletedAt(at) : Annotation,
            ReadAt = at,
        };
    }

    private static StorytellerSubscriptionStatus ToSubscriptionStatus(AnnotationStatus status)
    {
        return status switch
        {
            AnnotationStatus.Disabled => StorytellerSubscriptionStatus.Disabled,
            AnnotationStatus.Scheduled => StorytellerSubscriptionStatus.Scheduled,
            AnnotationStatus.Expired => StorytellerSubscriptionStatus.Expired,
            _ => StorytellerSubscriptionStatus.Active,
        };
    }

    private StorytellerAnnotation? Find(AnnotationType type)
    {
        return Annotation.Type == type
            ? Annotation
            : Ancestors.GetValueOrDefault(type);
    }

    private bool IsUnreadable(AnnotationType type)
    {
        return Find(type) is null
            && Unreadable.Any(key => FullKey.TryParse(key, out var fullKey) && fullKey.Annotation.Type == type);
    }
}
