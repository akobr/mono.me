using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.Flattening;

// Turns the Content object of a configuration into IConfiguration keys: properties join with ':', array items with
// their index (db:hosts:0); a string is its value, a number or a boolean its JSON text; null and empty containers make
// no key; names keep their casing. Configuration/test/Vectors/flattening.json pins the rules for every language.
internal static class DocumentFlattener
{
    public const string NotAnObjectRule = "not-an-object";
    public const string ColonInNameRule = "colon-in-name";
    public const string EmptyNameRule = "empty-name";
    public const string CaseCollisionRule = "case-collision";

    // Case-insensitive, as IConfiguration compares keys. A null content, which a version-0 configuration may have, has
    // no keys.
    public static IReadOnlyDictionary<string, string> Flatten(JToken? content)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (content is null || content.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return data;
        }

        if (content is not JObject root)
        {
            throw new InvalidDocumentException(
                NotAnObjectRule,
                JsonPaths.Root,
                $"The content of the configuration, {JsonPaths.Root}, must be a JSON object, not {DescribeType(content.Type)}.");
        }

        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Visit(root, null, JsonPaths.Root, data, paths);
        return data;
    }

    private static void Visit(JToken token, string? key, string path, Dictionary<string, string> data, Dictionary<string, string> paths)
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
                    if (name.Length == 0)
                    {
                        throw new InvalidDocumentException(EmptyNameRule, propertyPath, $"The property at {propertyPath} has an empty name.");
                    }

                    if (name.Contains(':', StringComparison.Ordinal))
                    {
                        throw new InvalidDocumentException(
                            ColonInNameRule,
                            propertyPath,
                            $"The name of the property at {propertyPath} contains ':', the separator of IConfiguration keys.");
                    }

                    Visit(property.Value, key is null ? name : $"{key}:{name}", propertyPath, data, paths);
                }

                break;

            case JArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    Visit(array[index], $"{key}:{index.ToString(CultureInfo.InvariantCulture)}", JsonPaths.Item(path, index), data, paths);
                }

                break;

            case JValue value when ToText(value) is { } text:
                // Only the root has no key, and the root is an object.
                if (paths.TryGetValue(key!, out var otherPath))
                {
                    throw new InvalidDocumentException(
                        CaseCollisionRule,
                        path,
                        $"The keys of {otherPath} and {path} differ only in case; IConfiguration compares keys case-insensitively.");
                }

                data.Add(key!, text);
                paths.Add(key!, path);
                break;
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
