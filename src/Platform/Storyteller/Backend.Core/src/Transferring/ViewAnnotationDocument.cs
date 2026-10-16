using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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

    // A JSON object with comments allowed and duplicate properties rejected; anything else throws Newtonsoft's
    // JsonException. Strings that look like dates stay strings, as the annotation reads (System.Text.Json) keep them,
    // so an exported annotation reads back equal.
    public static JObject Parse(Stream stream)
    {
        return ViewDocumentJson.Parse(stream, DateParseHandling.None);
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
        switch (value)
        {
            case null:
            case JsonElement { ValueKind: JsonValueKind.Undefined }:
                return JValue.CreateNull();

            case JToken token:
                return token.DeepClone();
        }

        var raw = value is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(value, value.GetType());
        using var reader = new JsonTextReader(new StringReader(raw)) { DateParseHandling = DateParseHandling.None };
        return JToken.ReadFrom(reader);
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
