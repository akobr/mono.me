using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// An annotation as the catalog read it, from a point read: list items carry lossy values. Its status is evaluated against
// a time, as it changes when ValidFrom or ExpiresAt passes. A snapshot is shared by every caller, so what a caller could
// change (Values, Created, Modified) is a copy per access. ToString() leaves the values out.
public class StorytellerAnnotation
{
    // Without the application's JsonConvert.DefaultSettings.
    private static readonly JsonSerializer Serializer = JsonSerializer.Create();

    private static readonly IReadOnlyDictionary<string, object?> NoValues = ReadOnlyDictionary<string, object?>.Empty;

    private readonly IReadOnlyDictionary<string, object?> _values = NoValues;
    private readonly AuditStamp? _created;
    private readonly AuditStamp? _modified;

    public StorytellerAnnotation()
    {
    }

    // A copy that differs only in what its initializer sets.
    [SetsRequiredMembers]
    private protected StorytellerAnnotation(StorytellerAnnotation source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Key = source.Key;
        AnnotationKey = source.AnnotationKey;
        Type = source.Type;
        Name = source.Name;
        Title = source.Title;
        Description = source.Description;
        Labels = source.Labels;
        IsDisabled = source.IsDisabled;
        ValidFrom = source.ValidFrom;
        ExpiresAt = source.ExpiresAt;
        DeletedAt = source.DeletedAt;
        _values = source._values;
        _created = source._created;
        _modified = source._modified;
    }

    // The full key, @{organization}.{project}.{view}.{annotation key}, with the type code lower-cased, as the keys of
    // the documents have it.
    public required string Key { get; init; }

    public required string AnnotationKey { get; init; }

    // From the key.
    public required AnnotationType Type { get; init; }

    // From the key: its last segment.
    public required string Name { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Labels { get; internal init; } = [];

    // What the SDK read: strings (date-like ones included), numbers, booleans, nulls and JSON tokens. A deep copy per
    // access.
    public IReadOnlyDictionary<string, object?> Values
    {
        get => CopyValues(_values);
        internal init => _values = value ?? NoValues;
    }

    public bool IsDisabled { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    // The SDK's AuditStamp; a copy per access.
    public AuditStamp? Created
    {
        get => CopyStamp(_created);
        internal init => _created = value;
    }

    public AuditStamp? Modified
    {
        get => CopyStamp(_modified);
        internal init => _modified = value;
    }

    // Set only on a deleted annotation, which only who am I reads.
    public DateTimeOffset? DeletedAt { get; init; }

    // Disabled wins, then a ValidFrom after now (scheduled), then an ExpiresAt at or before now (expired). Nothing comes
    // from the ancestors.
    public AnnotationStatus GetStatus(DateTimeOffset now)
    {
        return AnnotationStatusExtensions.GetStatus(IsDisabled, ValidFrom, ExpiresAt, now);
    }

    public override string ToString()
    {
        return $"{Key} ({Type})";
    }

    // The same annotation, deleted at a time: who am I when its key is gone.
    internal virtual StorytellerAnnotation WithDeletedAt(DateTimeOffset deletedAt)
    {
        return new StorytellerAnnotation(this) { DeletedAt = deletedAt };
    }

    // JSON tokens are mutable; strings, numbers and booleans are not.
    internal static IReadOnlyDictionary<string, object?> CopyValues(IEnumerable<KeyValuePair<string, object?>>? values)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (name, value) in values ?? [])
        {
            copy[name] = value is JToken token ? token.DeepClone() : value;
        }

        return copy.AsReadOnly();
    }

    // Through JSON tokens, which keep the DateTimeOffset as it is.
    internal static AuditStamp? CopyStamp(AuditStamp? stamp)
    {
        return stamp is null
            ? null
            : JToken.FromObject(stamp, Serializer).ToObject<AuditStamp>(Serializer);
    }
}
