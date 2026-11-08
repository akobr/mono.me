using System.Text.Json;
using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Annotating;

// The editable document of an annotation, the JSON object a patch applies to: every editable member present,
// null when unset, Labels [] and Values {} when there are none.
internal static class AnnotationPatchDocument
{
    private static readonly IReadOnlyList<string> CommonMembers =
    [
        nameof(Annotation.IsDisabled),
        nameof(Annotation.ValidFrom),
        nameof(Annotation.ExpiresAt),
        nameof(Annotation.TimeZone),
        nameof(Annotation.Title),
        nameof(Annotation.Description),
        nameof(Annotation.DocumentationLink),
        nameof(Annotation.Labels),
        nameof(Annotation.Values),
    ];

    private static readonly IReadOnlyList<string> UnitMembers =
    [
        .. CommonMembers,
        nameof(Unit.UnitType),
        nameof(Unit.UnitDefinition),
    ];

    public static IReadOnlyList<string> GetMembers(Annotation annotation)
    {
        return annotation is Unit ? UnitMembers : CommonMembers;
    }

    public static JsonObject From(Annotation annotation)
    {
        var document = new JsonObject
        {
            [nameof(Annotation.IsDisabled)] = annotation.IsDisabled is { } isDisabled ? JsonValue.Create(isDisabled) : null,
            [nameof(Annotation.ValidFrom)] = ToDate(annotation.ValidFrom),
            [nameof(Annotation.ExpiresAt)] = ToDate(annotation.ExpiresAt),
            [nameof(Annotation.TimeZone)] = annotation.TimeZone,
            [nameof(Annotation.Title)] = annotation.Title,
            [nameof(Annotation.Description)] = annotation.Description,
            [nameof(Annotation.DocumentationLink)] = annotation.DocumentationLink,
            [nameof(Annotation.Labels)] = new JsonArray((annotation.Labels ?? []).Select(label => (JsonNode?)label).ToArray()),
            [nameof(Annotation.Values)] = ToValues(annotation.Values),
        };

        if (annotation is Unit unit)
        {
            document[nameof(Unit.UnitType)] = unit.UnitType;
            document[nameof(Unit.UnitDefinition)] = unit.UnitDefinition;
        }

        return document;
    }

    // The editable members of the document over the current annotation. A missing member is unset.
    public static Annotation ToAnnotation(JsonObject document, Annotation current)
    {
        var members = GetMembers(current);

        foreach (var (name, _) in document)
        {
            if (!members.Contains(name, StringComparer.Ordinal))
            {
                throw Invalid($"The patched annotation has an unknown member '{name}'.");
            }
        }

        var annotation = current with
        {
            IsDisabled = ReadBoolean(document, nameof(Annotation.IsDisabled)),
            ValidFrom = ReadDate(document, nameof(Annotation.ValidFrom)),
            ExpiresAt = ReadDate(document, nameof(Annotation.ExpiresAt)),
            TimeZone = ReadString(document, nameof(Annotation.TimeZone)),
            Title = ReadString(document, nameof(Annotation.Title)),
            Description = ReadString(document, nameof(Annotation.Description)),
            DocumentationLink = ReadString(document, nameof(Annotation.DocumentationLink)),
            Labels = ReadLabels(document),
            Values = ReadValues(document),
        };

        return annotation is Unit unit
            ? unit with
            {
                UnitType = ReadUnitMember(document, nameof(Unit.UnitType)),
                UnitDefinition = ReadUnitMember(document, nameof(Unit.UnitDefinition)),
            }
            : annotation;
    }

    private static JsonNode? ToDate(DateTimeOffset? value)
    {
        // as System.Text.Json writes it: ISO 8601 with the offset kept
        return value is { } date ? JsonSerializer.SerializeToNode(date) : null;
    }

    private static JsonObject ToValues(IReadOnlyDictionary<string, object>? values)
    {
        var document = new JsonObject();

        foreach (var (key, value) in values ?? new Dictionary<string, object>())
        {
            document[key] = ToNode(value);
        }

        return document;
    }

    // A value as the reads hold it (JsonElement) or a plain CLR object; objects become JsonObjects, so they merge.
    private static JsonNode? ToNode(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => null,
            JsonElement element => JsonSerializer.SerializeToNode(element),
            _ => JsonSerializer.SerializeToNode(value, value.GetType()),
        };
    }

    private static bool? ReadBoolean(JsonObject document, string member)
    {
        var node = Get(document, member);

        return node?.GetValueKind() switch
        {
            null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid($"The member '{member}' must be a boolean or null."),
        };
    }

    private static DateTimeOffset? ReadDate(JsonObject document, string member)
    {
        var node = Get(document, member);

        if (node is null)
        {
            return null;
        }

        if (node.GetValueKind() == JsonValueKind.String)
        {
            try
            {
                return node.Deserialize<DateTimeOffset>();
            }
            catch (JsonException)
            {
                // reported below
            }
        }

        throw Invalid($"The member '{member}' must be an ISO 8601 date and time or null.");
    }

    private static string? ReadString(JsonObject document, string member)
    {
        var node = Get(document, member);

        if (node is null)
        {
            return null;
        }

        return node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : throw Invalid($"The member '{member}' must be a string or null.");
    }

    // A unit always has its type and definition.
    private static string ReadUnitMember(JsonObject document, string member)
    {
        var node = Get(document, member)
            ?? throw Invalid($"A unit always has a {member}: the member '{member}' cannot be removed or null.");

        return node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : throw Invalid($"The member '{member}' must be a string.");
    }

    // The first occurrence of each label, in order.
    private static IReadOnlyList<string>? ReadLabels(JsonObject document)
    {
        const string member = nameof(Annotation.Labels);
        var node = Get(document, member);

        if (node is null)
        {
            return null;
        }

        if (node is not JsonArray array)
        {
            throw Invalid($"The member '{member}' must be an array of strings or null.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var labels = new List<string>(array.Count);

        for (var index = 0; index < array.Count; index++)
        {
            var item = array[index];

            if (item is null || item.GetValueKind() != JsonValueKind.String)
            {
                throw Invalid($"The member '{member}' must be an array of strings: the item {index} is not a string.");
            }

            var label = item.GetValue<string>();

            if (seen.Add(label))
            {
                labels.Add(label);
            }
        }

        return labels;
    }

    // As System.Text.Json reads a body: a JsonElement for each value, null for a JSON null.
    private static IReadOnlyDictionary<string, object>? ReadValues(JsonObject document)
    {
        const string member = nameof(Annotation.Values);
        var node = Get(document, member);

        return node switch
        {
            null => null,
            JsonObject values => values.Deserialize<Dictionary<string, object>>() ?? new Dictionary<string, object>(),
            _ => throw Invalid($"The member '{member}' must be an object or null."),
        };
    }

    private static JsonNode? Get(JsonObject document, string member)
    {
        return document.TryGetPropertyValue(member, out var node) ? node : null;
    }

    private static InvalidInputException Invalid(string message)
    {
        return new InvalidInputException(message, ErrorCodes.InvalidAnnotation);
    }
}
