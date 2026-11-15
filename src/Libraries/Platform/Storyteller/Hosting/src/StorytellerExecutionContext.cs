using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// Who am I: the annotation of the key and those of its ancestors that were read, as of ReadAt. A key of any type: an
// execution (a service of one subject), a unit of execution (a job), or a responsibility (a worker that discovers its
// subjects through the catalog). A read puts the typed snapshot of each type here (StorytellerResponsibility,
// StorytellerUnit, …), with the names of its relations; the members stay typed StorytellerAnnotation, so a test double
// may put base snapshots here, and Find<T>() gives the typed one. Immutable: Ancestors and Unreadable are copied from an
// initializer, so an application can build one for a test double of who am I and change its own collections
// afterwards.
public sealed class StorytellerExecutionContext
{
    // The chain order of the ancestors: the nearer one first; at the same level the place before the capability
    // (context, then usage, then unit), and the subject before the responsibility.
    private static readonly AnnotationType[] ChainOrder =
    [
        AnnotationType.UnitOfExecution,
        AnnotationType.Execution,
        AnnotationType.Context,
        AnnotationType.Usage,
        AnnotationType.Unit,
        AnnotationType.Subject,
        AnnotationType.Responsibility,
    ];

    private readonly IReadOnlyDictionary<AnnotationType, StorytellerAnnotation> _ancestors = ReadOnlyDictionary<AnnotationType, StorytellerAnnotation>.Empty;
    private readonly IReadOnlyList<string> _unreadable = [];

    // Built at the first access, once an initializer has set Annotation and Ancestors.
    private IReadOnlyList<StorytellerAnnotation>? _chain;

    public StorytellerExecutionContext()
    {
    }

    // A copy that differs only in what its initializer sets. The chain is not copied: the copy builds the one of its
    // own annotation.
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

    // Of the key: the snapshot of its type when read, such as a StorytellerExecution for an exe key.
    public required StorytellerAnnotation Annotation { get; init; }

    // The ancestors that were read, by their type, each the snapshot of its type. One that the credential may not read
    // is in Unreadable; one deleted after the key was read is in neither. Never null.
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

    // The annotation of each type: the key's own, or an ancestor. A StorytellerSubject, StorytellerResponsibility,
    // StorytellerUsage or StorytellerContext when read; Find<T>() gives it typed.
    public StorytellerAnnotation? Subject => Find(AnnotationType.Subject);

    public StorytellerAnnotation? Responsibility => Find(AnnotationType.Responsibility);

    public StorytellerAnnotation? Usage => Find(AnnotationType.Usage);

    public StorytellerAnnotation? Context => Find(AnnotationType.Context);

    // The key's own for an exe key, its parent for a uxe key.
    public StorytellerExecution? Execution => Find(AnnotationType.Execution) as StorytellerExecution;

    // The key's annotation and its ancestors that were read, nearest first: the key's own, then the unit of execution,
    // the execution, the context, the usage, the unit, the subject and the responsibility. It comes from Annotation and
    // Ancestors, so a test double has it too, and an ancestor that is unreadable or missing is not in it. Never null,
    // read-only.
    public IReadOnlyList<StorytellerAnnotation> Chain
    {
        get
        {
            if (_chain is { } chain)
            {
                return chain;
            }

            // Calls at once build equal chains; the first one stays, for every caller.
            var built = BuildChain();
            return Interlocked.CompareExchange(ref _chain, built, null) ?? built;
        }
    }

    // The annotation of the type of TAnnotation, the key's own or an ancestor, such as Find<StorytellerUnit>() for the
    // unit of a uxe key: null when it was not read, or when it is not a TAnnotation (a test double's base snapshot).
    // StorytellerAnnotation itself names no type and throws ArgumentException.
    public TAnnotation? Find<TAnnotation>()
        where TAnnotation : StorytellerAnnotation
    {
        return Find(SnapshotTypes.Require<TAnnotation>(nameof(TAnnotation))) as TAnnotation;
    }

    // The time zone that applies to the key: the first TimeZone that is set along Chain, as stored, so the nearest
    // annotation that sets one wins; null when none is set or read. It reads nothing: an ancestor in Unreadable is
    // skipped, so a restricted machine may get another answer than an unrestricted one. The fallback is the caller's,
    // such as ?? "UTC", as the project's default time zone is not in the API. Not the rule of the configuration, whose
    // scalars take the subject after the usage, and the responsibility after the unit.
    public string? GetEffectiveTimeZone()
    {
        foreach (var annotation in Chain)
        {
            if (!string.IsNullOrWhiteSpace(annotation.TimeZone))
            {
                return annotation.TimeZone;
            }
        }

        return null;
    }

    // Is it active: Deleted when the key's annotation or its execution is deleted; else the first status that is not
    // active among the key's annotation, its execution and its usage; Unknown when all that were read are active but
    // the usage (or the execution of a unit of execution) was unreadable; else Active. A key without an execution in its
    // chain (rst, sbt, unt, usg, cnt) has the status of its own annotation.
    public StorytellerSubscriptionStatus GetSubscriptionStatus(DateTimeOffset now)
    {
        return Decide(now, StorytellerStatusScope.Subscription).Status;
    }

    // The same in a scope. Subscription is GetSubscriptionStatus(now). Chain counts the key's annotation and every
    // ancestor: Deleted when the key's annotation or its execution is deleted; else the status of the first annotation of
    // Chain that is not active; else Unknown when an ancestor is in Unreadable; else Active. With Chain a key of every
    // type counts its ancestors, so a usage under a disabled subject is Disabled. It reads nothing. A scope that is not
    // defined throws ArgumentOutOfRangeException.
    public StorytellerSubscriptionStatus GetSubscriptionStatus(DateTimeOffset now, StorytellerStatusScope scope)
    {
        return Decide(now, scope).Status;
    }

    // The annotation that decides a status other than Active and Unknown, the first one in the order of the scope, so a
    // log can name it: "exe.… is not run: sbt.… is disabled". For Deleted the deleted one: the key's own when it is
    // deleted, else its execution. null for Active and Unknown. A scope that is not defined throws
    // ArgumentOutOfRangeException.
    public StorytellerAnnotation? FindInactiveAnnotation(DateTimeOffset now, StorytellerStatusScope scope)
    {
        return Decide(now, scope).Annotation;
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

    // The key's annotation, then the ancestors in the chain order. An ancestor of the key's own type, which only a test
    // double can put there, is left out, as Find leaves it out.
    private IReadOnlyList<StorytellerAnnotation> BuildChain()
    {
        var chain = new List<StorytellerAnnotation>(_ancestors.Count + 1) { Annotation };

        foreach (var type in ChainOrder)
        {
            if (type != Annotation.Type && _ancestors.GetValueOrDefault(type) is { } ancestor)
            {
                chain.Add(ancestor);
            }
        }

        return chain.AsReadOnly();
    }

    // The status in a scope and the annotation that decides it, from one routine, so GetSubscriptionStatus and
    // FindInactiveAnnotation cannot disagree.
    private (StorytellerSubscriptionStatus Status, StorytellerAnnotation? Annotation) Decide(DateTimeOffset now, StorytellerStatusScope scope)
    {
        if (scope is not (StorytellerStatusScope.Subscription or StorytellerStatusScope.Chain))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown status scope.");
        }

        var execution = Execution;

        // Chain asks the execution that its chain holds, a test double's base snapshot too; Subscription the typed one,
        // as GetSubscriptionStatus(now) always did.
        var deletable = scope == StorytellerStatusScope.Chain ? Find(AnnotationType.Execution) : execution;

        if (Annotation.DeletedAt is not null)
        {
            return (StorytellerSubscriptionStatus.Deleted, Annotation);
        }

        if (deletable?.DeletedAt is not null)
        {
            return (StorytellerSubscriptionStatus.Deleted, deletable);
        }

        foreach (var annotation in Counted(scope, execution))
        {
            if (annotation?.GetStatus(now) is { } status and not AnnotationStatus.Active)
            {
                return (ToSubscriptionStatus(status), annotation);
            }
        }

        return (HasUnreadable(scope) ? StorytellerSubscriptionStatus.Unknown : StorytellerSubscriptionStatus.Active, null);
    }

    // The annotations that a scope counts, in its order. Subscription: a key without an execution in its chain counts its
    // own annotation alone.
    private IReadOnlyList<StorytellerAnnotation?> Counted(StorytellerStatusScope scope, StorytellerExecution? execution)
    {
        if (scope == StorytellerStatusScope.Chain)
        {
            return Chain;
        }

        return HasExecution() ? [Annotation, execution, Usage] : [Annotation];
    }

    // Whether the credential may not read an annotation that the scope counts. Chain: any ancestor, by the rule of
    // IsUnreadable: an entry of Unreadable counts when it is a full key of a type the context holds no annotation of.
    private bool HasUnreadable(StorytellerStatusScope scope)
    {
        if (scope == StorytellerStatusScope.Chain)
        {
            return Unreadable.Any(key => FullKey.TryParse(key, out var fullKey) && Find(fullKey.Annotation.Type) is null);
        }

        return HasExecution() && (IsUnreadable(AnnotationType.Usage) || IsUnreadable(AnnotationType.Execution));
    }

    // An execution is in the chain of the key: its own, or the one of a unit of execution.
    private bool HasExecution()
    {
        return Annotation.Type is AnnotationType.Execution or AnnotationType.UnitOfExecution;
    }

    private bool IsUnreadable(AnnotationType type)
    {
        return Find(type) is null
            && Unreadable.Any(key => FullKey.TryParse(key, out var fullKey) && fullKey.Annotation.Type == type);
    }
}
