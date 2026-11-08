using System.Text;
using _42.Platform.Storyteller.Sdk.Hosting.Informer;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using static _42.Platform.Storyteller.Sdk.Hosting.Reading.AnnotationTypeFacts;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A target of the catalog: the descendants of Type under the annotation Of, active ones only unless IncludingInactive(),
// deleted ones never. The filters narrow them by a name of their keys, ordinally, in the process: the API has no filter
// of descendants, so a filter does not make the list cheaper. Immutable: each With… returns a new target, and checks its
// name at the call.
public sealed class StorytellerTarget<TAnnotation>
    where TAnnotation : StorytellerAnnotation
{
    // type: the descendants' type, which TAnnotation is the snapshot of; the factories of StorytellerTarget pair them.
    internal StorytellerTarget(string key, AnnotationType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));

        var text = key.Trim();

        if (text.StartsWith(Constants.FullKeyStartChar))
        {
            FullAncestorKey = FullKey.TryParse(text, out var fullKey)
                ? CatalogKeys.Normalize(fullKey)
                : throw new ArgumentException($"'{text}' is not a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}.", nameof(key));
            AncestorKey = FullAncestorKey.Annotation;
            Of = FullAncestorKey.ToString();
        }
        else
        {
            AncestorKey = AnnotationKey.TryParse(text, out var annotationKey)
                ? CatalogKeys.Normalize(annotationKey)
                : throw new ArgumentException($"'{text}' is neither a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}, nor an annotation key.", nameof(key));
            Of = AncestorKey.ToString();
        }

        Type = type;
        var descendants = DescendantsOf(AncestorKey.Type);

        if (descendants.Count == 0)
        {
            throw new ArgumentException($"{Of} is {Singular(AncestorKey.Type)}, which has no descendants.", nameof(key));
        }

        if (!descendants.Contains(type))
        {
            throw new ArgumentException(
                $"{Of} has no {Plural(type)}: the descendants of {Singular(AncestorKey.Type)} are its {Join(descendants.Select(Plural))}.",
                nameof(key));
        }
    }

    // A copy that differs only in what its initializer sets.
    private StorytellerTarget(StorytellerTarget<TAnnotation> source)
    {
        Of = source.Of;
        Type = source.Type;
        AncestorKey = source.AncestorKey;
        FullAncestorKey = source.FullAncestorKey;
        Subject = source.Subject;
        Responsibility = source.Responsibility;
        Context = source.Context;
        Unit = source.Unit;
        IncludeInactive = source.IncludeInactive;
    }

    // The ancestor's key as given, a full key or an annotation key, trimmed, with the type code lower-cased.
    public string Of { get; }

    // The type of the descendants.
    public AnnotationType Type { get; }

    // The filters, each a name of the descendants' keys; null when not set.
    public string? Subject { get; private init; }

    public string? Responsibility { get; private init; }

    public string? Context { get; private init; }

    public string? Unit { get; private init; }

    // Disabled, scheduled and expired descendants too; deleted ones never.
    public bool IncludeInactive { get; private init; }

    internal AnnotationKey AncestorKey { get; }

    // Set when Of is a full key; an annotation key takes the scope of the options' Key at the call.
    internal FullKey? FullAncestorKey { get; }

    // The descendants of one subject. Throws ArgumentException for a name that is empty or has a dot, for descendants
    // whose keys name no subject (units), and for an ancestor that names it already (sbt, usg, cnt, exe).
    public StorytellerTarget<TAnnotation> WithSubject(string subject)
    {
        Check(KeyParts.Subject, subject, nameof(subject));
        return new StorytellerTarget<TAnnotation>(this) { Subject = subject };
    }

    // The descendants of one responsibility; not for contexts, nor under rst, unt, usg or exe.
    public StorytellerTarget<TAnnotation> WithResponsibility(string responsibility)
    {
        Check(KeyParts.Responsibility, responsibility, nameof(responsibility));
        return new StorytellerTarget<TAnnotation>(this) { Responsibility = responsibility };
    }

    // The descendants in one context; not for usages and units, nor under cnt or exe.
    public StorytellerTarget<TAnnotation> WithContext(string context)
    {
        Check(KeyParts.Context, context, nameof(context));
        return new StorytellerTarget<TAnnotation>(this) { Context = context };
    }

    // The descendants of one unit: units and units of execution; not under unt.
    public StorytellerTarget<TAnnotation> WithUnit(string unit)
    {
        Check(KeyParts.Unit, unit, nameof(unit));
        return new StorytellerTarget<TAnnotation>(this) { Unit = unit };
    }

    public StorytellerTarget<TAnnotation> IncludingInactive()
    {
        return new StorytellerTarget<TAnnotation>(this) { IncludeInactive = true };
    }

    // "the executions of sbt.northwind in the context production".
    public override string ToString()
    {
        return Describe(Of);
    }

    // ancestor: the text that names the ancestor, as given or as resolved.
    internal string Describe(string ancestor)
    {
        var text = new StringBuilder($"the {Plural(Type)} of {ancestor}");

        if (Subject is not null)
        {
            text.Append($" of the subject {Subject}");
        }

        if (Responsibility is not null)
        {
            text.Append($" of the responsibility {Responsibility}");
        }

        if (Context is not null)
        {
            text.Append($" in the context {Context}");
        }

        if (Unit is not null)
        {
            text.Append($" of the unit {Unit}");
        }

        if (IncludeInactive)
        {
            text.Append(", inactive ones included");
        }

        return text.ToString();
    }

    // A key of the descendants' type that has the names of the filters.
    internal bool Passes(AnnotationKey key)
    {
        return Matches(key, KeyParts.Subject, Subject)
            && Matches(key, KeyParts.Responsibility, Responsibility)
            && Matches(key, KeyParts.Context, Context)
            && Matches(key, KeyParts.Unit, Unit);
    }

    private static bool Matches(AnnotationKey key, KeyParts part, string? name)
    {
        return name is null || string.Equals(NameOf(key, part), name, StringComparison.Ordinal);
    }

    // "a, b and c".
    private static string Join(IEnumerable<string> words)
    {
        var list = words.ToList();
        return list.Count < 2 ? string.Concat(list) : $"{string.Join(", ", list.SkipLast(1))} and {list[^1]}";
    }

    private void Check(KeyParts part, string name, string parameterName)
    {
        SubscriptionScope.ValidateName(name, parameterName);

        if ((PartsOf(Type) & part) == 0)
        {
            throw new ArgumentException(
                $"The keys of {Plural(Type)}, {Pattern(Type)}, name no {Word(part)}, so they have no {Word(part)} to filter by.",
                parameterName);
        }

        if ((PartsOf(AncestorKey.Type) & part) != 0)
        {
            throw new ArgumentException(
                $"{Of} names the {Word(part)} of its {Plural(Type)} already, {NameOf(AncestorKey, part)}; leave out the filter.",
                parameterName);
        }
    }
}
