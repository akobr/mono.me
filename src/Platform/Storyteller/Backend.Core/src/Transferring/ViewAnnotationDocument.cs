using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using _42.Platform.Storyteller.Annotating;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace _42.Platform.Storyteller.Transferring;

// The file of an annotation in an archive: its editable members (AnnotationFactory.Normalize) in the API's PascalCase
// names, null members left out. The key, the type and the names are in the file name, the stamps in the manifest;
// the relations, the deletion and the audit stay with the platform. Annotations have no merge, so $remove and $patch
// are not directives here.
public static class ViewAnnotationDocument
{
    // The editable members of every annotation, in the order they are written.
    private static readonly string[] Members =
    [
        nameof(Annotation.Title),
        nameof(Annotation.Description),
        nameof(Annotation.DocumentationLink),
        nameof(Annotation.Labels),
        nameof(Annotation.Values),
        nameof(Annotation.IsDisabled),
        nameof(Annotation.ValidFrom),
        nameof(Annotation.ExpiresAt),
        nameof(Annotation.TimeZone),
    ];

    // The editable members of units only.
    private static readonly string[] UnitMembers = [nameof(Unit.UnitType), nameof(Unit.UnitDefinition)];

    // Per type: the editable members, and every other member of the type's model, which is what a GET response
    // has besides them (the key and names, the relations, the stamps, the deletion).
    private static readonly Dictionary<AnnotationType, MemberSet> MemberSets = Enum.GetValues<AnnotationType>()
        .ToDictionary(type => type, MemberSet.Create);

    private static readonly JsonSerializerOptions ContentOptions = new();

    // The rules of ViewDocumentJson.Parse; a trailing comma is accepted there too.
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = ViewDocumentJson.MaxDepth,
    };

    // A JSON object with comments allowed and duplicate properties rejected; anything else throws Newtonsoft's
    // JsonException. It is read as the annotation reads (System.Text.Json) read it, so an exported annotation reads
    // back equal: strings that look like dates stay strings, and numbers keep their text (JRaw), which a double would
    // round and a decimal refuse beyond its range.
    public static JObject Parse(Stream stream)
    {
        string text;

        using (var reader = new StreamReader(stream, ViewDocumentJson.Utf8, detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        var json = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), ReaderOptions);

        try
        {
            if (!json.Read() || json.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonReaderException("The document is not a JSON object.");
            }

            var document = ReadObject(ref json);

            if (json.Read())
            {
                throw new JsonReaderException("The document continues after its JSON object.");
            }

            return document;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // a syntax error, or an escaped string that is not valid UTF-16
            throw new JsonReaderException(exception.Message, exception);
        }
    }

    public static JObject Write(Annotation annotation)
    {
        var document = new JObject();
        AddText(document, nameof(Annotation.Title), annotation.Title);
        AddText(document, nameof(Annotation.Description), annotation.Description);
        AddText(document, nameof(Annotation.DocumentationLink), annotation.DocumentationLink);

        if (annotation.Labels is not null)
        {
            document.Add(nameof(Annotation.Labels), new JArray(annotation.Labels.Select(label => new JValue(label))));
        }

        if (annotation.Values is not null)
        {
            var values = new JObject();

            foreach (var (name, value) in annotation.Values)
            {
                values.Add(name, ToToken(value));
            }

            document.Add(nameof(Annotation.Values), values);
        }

        if (annotation.IsDisabled is { } isDisabled)
        {
            document.Add(nameof(Annotation.IsDisabled), isDisabled);
        }

        if (annotation.ValidFrom is { } validFrom)
        {
            document.Add(nameof(Annotation.ValidFrom), new JValue(validFrom));
        }

        if (annotation.ExpiresAt is { } expiresAt)
        {
            document.Add(nameof(Annotation.ExpiresAt), new JValue(expiresAt));
        }

        AddText(document, nameof(Annotation.TimeZone), annotation.TimeZone);

        if (annotation is Unit unit)
        {
            AddText(document, nameof(Unit.UnitType), unit.UnitType);
            AddText(document, nameof(Unit.UnitDefinition), unit.UnitDefinition);
        }

        return document;
    }

    // The annotation a file holds, as a write stores it: a member missing from the file is cleared (an import
    // replaces the content), and a unit without UnitType or UnitDefinition gets the defaults of a create. Member names
    // match in any case, as in the API. The server-owned members of a GET response are ignored, so a response body
    // can be saved as a file. InvalidDocument: an AnnotationKey other than the key of the file name, any other member
    // (UnitType on a type other than a unit too), or a value of a wrong type. InvalidAnnotation: AnnotationRules.
    // The document should come from Parse.
    public static bool TryRead(
        JObject document,
        FullKey key,
        [NotNullWhen(true)] out Annotation? annotation,
        [NotNullWhen(false)] out string? errorCode,
        [NotNullWhen(false)] out string? message)
    {
        (annotation, errorCode, message) = Read(document, key);
        return annotation is not null;
    }

    private static (Annotation? Annotation, string? ErrorCode, string? Message) Read(JObject document, FullKey key)
    {
        var members = MemberSets[key.Annotation.Type];
        var content = new JObject();
        var unknown = new List<string>();

        foreach (var property in document.Properties())
        {
            if (members.Editable.TryGetValue(property.Name, out var name))
            {
                if (content.ContainsKey(name))
                {
                    return (null, ErrorCodes.InvalidDocument, $"The member '{name}' is in the file twice.");
                }

                content.Add(name, property.Value.DeepClone());
            }
            else if (!members.Ignored.Contains(property.Name))
            {
                unknown.Add(property.Name);
            }
            else if (property.Name.Equals(nameof(Annotation.AnnotationKey), StringComparison.OrdinalIgnoreCase)
                     && !IsKey(property.Value, key.Annotation))
            {
                return (
                    null,
                    ErrorCodes.InvalidDocument,
                    $"The AnnotationKey {property.Value.ToString(Formatting.None)} is not {key.Annotation}, the key of the file name.");
            }
        }

        if (unknown.Count > 0)
        {
            return (
                null,
                ErrorCodes.InvalidDocument,
                unknown.Count == 1
                    ? $"Unknown member '{unknown[0]}'."
                    : $"Unknown members {string.Join(", ", unknown.Select(name => $"'{name}'"))}.");
        }

        AnnotationContent input;

        try
        {
            // the API's reader (System.Text.Json), so the values compare as the stored ones do
            input = JsonSerializer.Deserialize<AnnotationContent>(content.ToString(Formatting.None), ContentOptions)!;
        }
        catch (JsonException exception)
        {
            return (null, ErrorCodes.InvalidDocument, $"The file is not a valid annotation: {exception.Message}");
        }

        var annotation = AnnotationFactory.CreateDefault(key.Annotation, key.ProjectName, key.ViewName) with
        {
            Title = input.Title,
            Description = input.Description,
            DocumentationLink = input.DocumentationLink,
            Labels = input.Labels,
            Values = input.Values,
            IsDisabled = input.IsDisabled,
            ValidFrom = input.ValidFrom,
            ExpiresAt = input.ExpiresAt,
            TimeZone = input.TimeZone,
        };

        if (annotation is Unit unit)
        {
            annotation = unit with
            {
                UnitType = input.UnitType ?? AnnotationFactory.DefaultUnitType,
                UnitDefinition = input.UnitDefinition ?? AnnotationFactory.DefaultUnitDefinition,
            };
        }

        var problems = AnnotationRules.GetProblems(annotation);

        return problems.Count > 0
            ? (null, ErrorCodes.InvalidAnnotation, $"The annotation '{annotation.AnnotationKey}' is not valid: {string.Join(" ", problems)}")
            : (annotation, null, null);
    }

    // The type code may differ in case, as in the file name; the names may not.
    private static bool IsKey(JToken value, AnnotationKey key)
    {
        if (value.Type != JTokenType.String || !AnnotationKey.TryParse(value.Value<string>()!, out var parsed))
        {
            return false;
        }

        var text = parsed.ToString();
        var expected = key.ToString();

        return parsed.TypeCode.Equals(key.TypeCode, StringComparison.OrdinalIgnoreCase)
               && string.Equals(text[parsed.TypeCode.Length..], expected[key.TypeCode.Length..], StringComparison.Ordinal);
    }

    private static void AddText(JObject document, string name, string? value)
    {
        if (value is not null)
        {
            document.Add(name, value);
        }
    }

    // A value of Values: a JsonElement when read from Cosmos DB or the API, any object when built in code.
    private static JToken ToToken(object? value)
    {
        return value switch
        {
            null => JValue.CreateNull(),
            JToken token => token.DeepClone(),
            JsonElement element => FromElement(element),
            _ => FromElement(JsonSerializer.SerializeToElement(value, value.GetType())),
        };
    }

    // As Parse reads it back: strings stay strings, and numbers keep the text they are stored with.
    private static JToken FromElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var document = new JObject();

                foreach (var property in element.EnumerateObject())
                {
                    // the last of the same name wins, as in the reads
                    document[property.Name] = FromElement(property.Value);
                }

                return document;

            case JsonValueKind.Array:
                return new JArray(element.EnumerateArray().Select(item => FromElement(item)));

            case JsonValueKind.String:
                return new JValue(element.GetString());

            case JsonValueKind.Number:
                return new JRaw(element.GetRawText());

            case JsonValueKind.True:
            case JsonValueKind.False:
                return new JValue(element.GetBoolean());

            default:
                return JValue.CreateNull();
        }
    }

    private static JObject ReadObject(ref Utf8JsonReader json)
    {
        var document = new JObject();

        while (json.Read() && json.TokenType == JsonTokenType.PropertyName)
        {
            var name = json.GetString()!;

            if (document.ContainsKey(name))
            {
                throw new JsonReaderException($"The property '{name}' is in its object twice.");
            }

            json.Read();
            document.Add(name, ReadValue(ref json));
        }

        return document;
    }

    private static JToken ReadValue(ref Utf8JsonReader json)
    {
        switch (json.TokenType)
        {
            case JsonTokenType.StartObject:
                return ReadObject(ref json);

            case JsonTokenType.StartArray:
                var array = new JArray();

                while (json.Read() && json.TokenType != JsonTokenType.EndArray)
                {
                    array.Add(ReadValue(ref json));
                }

                return array;

            case JsonTokenType.String:
                return new JValue(json.GetString());

            case JsonTokenType.Number:
                return new JRaw(Encoding.UTF8.GetString(json.ValueSpan));

            case JsonTokenType.True:
            case JsonTokenType.False:
                return new JValue(json.GetBoolean());

            default:
                return JValue.CreateNull();
        }
    }

    private sealed record class MemberSet(IReadOnlyDictionary<string, string> Editable, IReadOnlySet<string> Ignored)
    {
        public static MemberSet Create(AnnotationType type)
        {
            var editable = (type == AnnotationType.Unit ? Members.Concat(UnitMembers) : Members)
                .ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
            var ignored = AnnotationTypes.GetRuntimeType(type)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Where(name => !editable.ContainsKey(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return new MemberSet(editable, ignored);
        }
    }

    // The editable members as the annotation API reads them.
    private sealed record class AnnotationContent
    {
        public string? Title { get; init; }

        public string? Description { get; init; }

        public string? DocumentationLink { get; init; }

        public IReadOnlyList<string>? Labels { get; init; }

        public IReadOnlyDictionary<string, object>? Values { get; init; }

        public bool? IsDisabled { get; init; }

        public DateTimeOffset? ValidFrom { get; init; }

        public DateTimeOffset? ExpiresAt { get; init; }

        public string? TimeZone { get; init; }

        public string? UnitType { get; init; }

        public string? UnitDefinition { get; init; }
    }
}
