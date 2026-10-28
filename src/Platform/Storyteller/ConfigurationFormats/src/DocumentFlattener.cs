using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats;

// Reads the Content object of a configuration into its leaves and IConfiguration keys: properties join with ':', array
// items with their index (db:hosts:0); a string is its value, a number or a boolean its JSON text; null and empty
// containers make no key; names keep their casing. The conformance suite, src/Platform/Storyteller/conformance, pins
// the rules for every language.
public static class DocumentFlattener
{
    // The leaves in document order. With validate, the base rules of every format, and the first error throws: the
    // content is an object, no name is empty or contains ':', and no two keys differ only in case. Without, no rule
    // applies, and a content that is not an object has no leaves. A null content, which a version-0 configuration may
    // have, has no leaves.
    public static IReadOnlyList<DocumentLeaf> Read(JToken? content, bool validate = true)
    {
        var leaves = new List<DocumentLeaf>();

        if (content is null || content.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return leaves;
        }

        if (content is not JObject root)
        {
            if (!validate)
            {
                return leaves;
            }

            throw new InvalidDocumentException(
                DocumentRules.NotAnObject,
                JsonPaths.Root,
                $"The content of the configuration, {JsonPaths.Root}, must be a JSON object, not {DescribeType(content.Type)}.");
        }

        var paths = validate ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : null;
        Visit(root, null, JsonPaths.Root, [], leaves, paths);
        return leaves;
    }

    // The ':' keys of Read, case-insensitive, as IConfiguration compares keys.
    public static IReadOnlyDictionary<string, string> Flatten(JToken? content)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var leaf in Read(content))
        {
            data.Add(ToKey(leaf.Segments), leaf.Value);
        }

        return data;
    }

    // db:hosts:0
    internal static string ToKey(IReadOnlyList<DocumentSegment> segments)
    {
        return string.Join(':', segments.Select(segment => segment.Name ?? segment.Index.ToString(CultureInfo.InvariantCulture)));
    }

    // segments: the steps to the token, shared by the whole walk; paths: the path of every key so far, null without
    // validation.
    private static void Visit(
        JToken token,
        string? key,
        string path,
        List<DocumentSegment> segments,
        List<DocumentLeaf> leaves,
        Dictionary<string, string>? paths)
    {
        switch (token)
        {
            case JObject container:
                foreach (var property in container.Properties())
                {
                    var name = property.Name;
                    var propertyPath = JsonPaths.Property(path, name);

                    // Every name is checked, also of a null or an empty container, so a document is valid or not
                    // whatever its values are.
                    if (paths is not null)
                    {
                        CheckName(name, propertyPath);
                    }

                    segments.Add(DocumentSegment.Property(name));
                    Visit(property.Value, key is null ? name : $"{key}:{name}", propertyPath, segments, leaves, paths);
                    segments.RemoveAt(segments.Count - 1);
                }

                break;

            case JArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    segments.Add(DocumentSegment.Item(index));
                    Visit(array[index], $"{key}:{index.ToString(CultureInfo.InvariantCulture)}", JsonPaths.Item(path, index), segments, leaves, paths);
                    segments.RemoveAt(segments.Count - 1);
                }

                break;

            case JValue value when ToText(value) is { } text:
                // Only the root has no key, and the root is an object.
                if (paths is not null)
                {
                    if (paths.TryGetValue(key!, out var otherPath))
                    {
                        throw new InvalidDocumentException(
                            DocumentRules.CaseCollision,
                            path,
                            $"The keys of {otherPath} and {path} differ only in case; IConfiguration compares keys case-insensitively.");
                    }

                    paths.Add(key!, path);
                }

                leaves.Add(new DocumentLeaf(segments.ToArray(), text, KindOf(value.Type), path));
                break;
        }
    }

    private static void CheckName(string name, string path)
    {
        if (name.Length == 0)
        {
            throw new InvalidDocumentException(DocumentRules.EmptyName, path, $"The property at {path} has an empty name.");
        }

        if (name.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDocumentException(
                DocumentRules.ColonInName,
                path,
                $"The name of the property at {path} contains ':', the separator of IConfiguration keys.");
        }
    }

    // null for a JSON null, which makes no key.
    private static string? ToText(JValue value)
    {
        return value.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => null,
            JTokenType.String => (string)value.Value!,

            // The JSON text: true, not True; 0.5 with the invariant culture. The SDK reads numbers as double, so 1.50
            // arrives as 1.5.
            JTokenType.Integer or JTokenType.Float or JTokenType.Boolean => value.ToString(Formatting.None),

            // Never from the SDK, which reads with DateParseHandling.None; a token built in code keeps its value.
            JTokenType.Date when value.Value is DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
            JTokenType.Date when value.Value is DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value.Value, CultureInfo.InvariantCulture),
        };
    }

    private static DocumentValueKind KindOf(JTokenType type)
    {
        return type switch
        {
            JTokenType.Integer or JTokenType.Float => DocumentValueKind.Number,
            JTokenType.Boolean => DocumentValueKind.Boolean,
            _ => DocumentValueKind.String,
        };
    }

    private static string DescribeType(JTokenType type)
    {
        return type switch
        {
            JTokenType.Array => "an array",
            JTokenType.String => "a string",
            JTokenType.Integer or JTokenType.Float => "a number",
            JTokenType.Boolean => "a boolean",
            _ => type.ToString().ToLowerInvariant(),
        };
    }
}
