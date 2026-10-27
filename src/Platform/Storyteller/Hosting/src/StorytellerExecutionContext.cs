using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// Who am I: the annotation of the key and those of its ancestors that were read, as of ReadAt. A key of any type: an
// execution (a service of one subject), a unit of execution (a job), or a responsibility (a worker that discovers its
// subjects through the catalog).
public sealed class StorytellerExecutionContext
{
    public StorytellerExecutionContext()
    {
    }

    // A copy that differs only in what its initializer sets.
    [SetsRequiredMembers]
    private StorytellerExecutionContext(StorytellerExecutionContext source)
    {
        Key = source.Key;
        Annotation = source.Annotation;
        Ancestors = source.Ancestors;
        Unreadable = source.Unreadable;
        ReadAt = source.ReadAt;
    }

    // The full key, with the type code lower-cased.
    public required string Key { get; init; }

    // Of the key; a StorytellerExecution for an exe key.
    public required StorytellerAnnotation Annotation { get; init; }

    // The ancestors that were read, by their type. One that the credential may not read is in Unreadable; one deleted
    // after the key was read is in neither.
    public IReadOnlyDictionary<AnnotationType, StorytellerAnnotation> Ancestors { get; init; } = ReadOnlyDictionary<AnnotationType, StorytellerAnnotation>.Empty;

    // The full keys of the ancestors the credential may not read (AnnotationRestricted): a machine restricted to its
    // annotation reads that annotation and its descendants only.
    public IReadOnlyList<string> Unreadable { get; init; } = [];

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
