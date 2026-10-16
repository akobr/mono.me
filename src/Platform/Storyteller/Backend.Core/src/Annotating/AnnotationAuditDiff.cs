using System.Text.Json;

namespace _42.Platform.Storyteller.Annotating;

public sealed record AnnotationChanges(
    IReadOnlyList<AnnotationFieldChange> Fields,
    AnnotationLabelChanges? Labels,
    IReadOnlyList<AnnotationValueChange> Values)
{
    public static AnnotationChanges None { get; } = new([], null, []);

    public bool IsEmpty => Fields.Count == 0 && Labels is null && Values.Count == 0;
}

// What an update changed, as stored in the audit. Default properties keep their previous and new value;
// Description and UnitDefinition only the fact of the change; Values keep primitives only. Strings are cut.
public static class AnnotationAuditDiff
{
    public static AnnotationChanges Compute(Annotation before, Annotation after, int maxValueLength)
    {
        var fields = new List<AnnotationFieldChange>();

        AddText(fields, nameof(Annotation.Title), before.Title, after.Title, maxValueLength);
        AddText(fields, nameof(Annotation.DocumentationLink), before.DocumentationLink, after.DocumentationLink, maxValueLength);

        if ((before.IsDisabled ?? false) != (after.IsDisabled ?? false))
        {
            fields.Add(new AnnotationFieldChange
            {
                Field = nameof(Annotation.IsDisabled),
                Previous = before.IsDisabled ?? false,
                Value = after.IsDisabled ?? false,
            });
        }

        AddInstant(fields, nameof(Annotation.ValidFrom), before.ValidFrom, after.ValidFrom);
        AddInstant(fields, nameof(Annotation.ExpiresAt), before.ExpiresAt, after.ExpiresAt);
        AddText(fields, nameof(Annotation.TimeZone), before.TimeZone, after.TimeZone, maxValueLength);
        AddFlag(fields, nameof(Annotation.Description), before.Description, after.Description);

        if (before is Unit beforeUnit && after is Unit afterUnit)
        {
            AddText(fields, nameof(Unit.UnitType), beforeUnit.UnitType, afterUnit.UnitType, maxValueLength);
            AddFlag(fields, nameof(Unit.UnitDefinition), beforeUnit.UnitDefinition, afterUnit.UnitDefinition);
        }

        return new AnnotationChanges(
            fields,
            CompareLabels(before.Labels, after.Labels),
            CompareValues(before.Values, after.Values, maxValueLength));
    }

    public static AnnotationValueKind GetKind(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => AnnotationValueKind.String,
            JsonValueKind.Number => AnnotationValueKind.Number,
            JsonValueKind.True or JsonValueKind.False => AnnotationValueKind.Boolean,
            JsonValueKind.Object => AnnotationValueKind.Object,
            JsonValueKind.Array => AnnotationValueKind.Array,
            _ => AnnotationValueKind.Null,
        };
    }

    private static void AddText(List<AnnotationFieldChange> fields, string field, string? before, string? after, int maxValueLength)
    {
        var previous = string.IsNullOrEmpty(before) ? null : before;
        var value = string.IsNullOrEmpty(after) ? null : after;

        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return;
        }

        fields.Add(new AnnotationFieldChange
        {
            Field = field,
            Previous = Cut(previous, maxValueLength, out var isPreviousTruncated),
            IsPreviousTruncated = isPreviousTruncated,
            Value = Cut(value, maxValueLength, out var isValueTruncated),
            IsValueTruncated = isValueTruncated,
        });
    }

    private static void AddInstant(List<AnnotationFieldChange> fields, string field, DateTimeOffset? before, DateTimeOffset? after)
    {
        if (before == after)
        {
            return;
        }

        fields.Add(new AnnotationFieldChange { Field = field, Previous = before, Value = after });
    }

    // The value is never stored, only the fact that it changed.
    private static void AddFlag(List<AnnotationFieldChange> fields, string field, string? before, string? after)
    {
        var previous = string.IsNullOrEmpty(before) ? null : before;
        var value = string.IsNullOrEmpty(after) ? null : after;

        if (!string.Equals(previous, value, StringComparison.Ordinal))
        {
            fields.Add(new AnnotationFieldChange { Field = field });
        }
    }

    private static AnnotationLabelChanges? CompareLabels(IReadOnlyList<string>? before, IReadOnlyList<string>? after)
    {
        var previous = before ?? [];
        var current = after ?? [];
        var added = current.Where(label => !previous.Contains(label, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
        var removed = previous.Where(label => !current.Contains(label, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();

        return added.Count == 0 && removed.Count == 0
            ? null
            : new AnnotationLabelChanges { Added = added, Removed = removed };
    }

    private static IReadOnlyList<AnnotationValueChange> CompareValues(
        IReadOnlyDictionary<string, object>? before,
        IReadOnlyDictionary<string, object>? after,
        int maxValueLength)
    {
        var previous = before ?? new Dictionary<string, object>();
        var current = after ?? new Dictionary<string, object>();
        var changes = new List<AnnotationValueChange>();

        foreach (var key in previous.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var hasPrevious = previous.TryGetValue(key, out var previousValue);
            var hasValue = current.TryGetValue(key, out var value);
            var previousElement = hasPrevious ? ToElement(previousValue) : (JsonElement?)null;
            var valueElement = hasValue ? ToElement(value) : (JsonElement?)null;

            if (previousElement is { } p && valueElement is { } v && (JsonElement.DeepEquals(p, v) || AreEqualAsStored(p, v)))
            {
                continue;
            }

            var change = new AnnotationValueChange
            {
                Key = key,
                Change = !hasPrevious
                    ? AnnotationValueChangeKind.Added
                    : !hasValue
                        ? AnnotationValueChangeKind.Removed
                        : AnnotationValueChangeKind.Changed,
            };

            if (previousElement is { } previousJson)
            {
                change = change with
                {
                    PreviousKind = GetKind(previousJson),
                    Previous = ToPrimitive(previousJson, maxValueLength, out var isPreviousTruncated),
                    IsPreviousTruncated = isPreviousTruncated,
                };
            }

            if (valueElement is { } valueJson)
            {
                change = change with
                {
                    ValueKind = GetKind(valueJson),
                    Value = ToPrimitive(valueJson, maxValueLength, out var isValueTruncated),
                    IsValueTruncated = isValueTruncated,
                };
            }

            changes.Add(change);
        }

        return changes;
    }

    // JsonElement.DeepEquals, except that two numbers are equal when they are the same double and one of them has a
    // fraction or an exponent. Cosmos DB stores such a number as a double and writes it with 17 digits in a point read
    // and with the fewest digits in a query (3.1415926535897931 and 3.141592653589793, 1e+300 and 1E+300), so an
    // exported annotation compares equal to the stored one. Integers compare exactly, beyond a double too.
    private static bool AreEqualAsStored(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Number:
                return JsonElement.DeepEquals(left, right)
                       || ((HasFractionOrExponent(left) || HasFractionOrExponent(right))
                           && left.TryGetDouble(out var leftNumber)
                           && right.TryGetDouble(out var rightNumber)
                           && double.IsFinite(leftNumber)
                           && leftNumber == rightNumber);

            case JsonValueKind.Array:
                return left.GetArrayLength() == right.GetArrayLength()
                       && left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => AreEqualAsStored(pair.First, pair.Second));

            case JsonValueKind.Object:
                return left.EnumerateObject().Count() == right.EnumerateObject().Count()
                       && left.EnumerateObject().All(property =>
                           right.TryGetProperty(property.Name, out var other) && AreEqualAsStored(property.Value, other));

            default:
                return JsonElement.DeepEquals(left, right);
        }
    }

    private static bool HasFractionOrExponent(JsonElement number)
    {
        return number.GetRawText().AsSpan().IndexOfAny('.', 'e', 'E') >= 0;
    }

    private static JsonElement ToElement(object? value)
    {
        return value switch
        {
            JsonElement element => element,
            null => JsonSerializer.SerializeToElement<object?>(null),
            _ => JsonSerializer.SerializeToElement(value, value.GetType()),
        };
    }

    // Objects and arrays are recorded only by their kind.
    private static object? ToPrimitive(JsonElement element, int maxValueLength, out bool isTruncated)
    {
        isTruncated = false;

        return element.ValueKind switch
        {
            JsonValueKind.String => Cut(element.GetString(), maxValueLength, out isTruncated),
            JsonValueKind.Number => element.Clone(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string? Cut(string? value, int maxValueLength, out bool isTruncated)
    {
        isTruncated = value is not null && value.Length > maxValueLength;
        return isTruncated ? value![..maxValueLength] : value;
    }
}
