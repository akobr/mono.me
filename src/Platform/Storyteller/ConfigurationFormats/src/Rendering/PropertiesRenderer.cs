using System.Buffers;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.Rendering;

// Spring Boot .properties: db.hosts[0]=a, one line per value. A name with other characters than ASCII letters, digits,
// - and _ is written in Spring's bracket form, .[name], which Spring binds with every character kept. The text is
// ASCII, as java.util.Properties.load(InputStream) reads ISO-8859-1.
internal static class PropertiesRenderer
{
    private static readonly SearchValues<char> PlainCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    // The name rule, then the value rule, each over the whole document.
    public static void Validate(JToken? content)
    {
        PropertyNames.Check(content, (name, path, _) =>
        {
            if (name.AsSpan().ContainsAny('[', ']'))
            {
                throw new InvalidDocumentException(
                    DocumentRules.BracketInName,
                    path,
                    $"The name of the property at {path} contains '[' or ']', which a properties key cannot hold.");
            }
        });

        if (content is JObject root)
        {
            CheckGaps(root, JsonPaths.Root);
        }
    }

    public static string Render(IReadOnlyList<DocumentLeaf> leaves)
    {
        var builder = new StringBuilder();

        foreach (var leaf in leaves)
        {
            AppendEscaped(builder, ToKey(leaf.Segments), true);
            builder.Append('=');
            AppendEscaped(builder, leaf.Value, false);
            builder.Append('\n');
        }

        return builder.ToString();
    }

    // The key a reader returns, before escaping: Logging.LogLevel.[Microsoft.AspNetCore], list[3].name, [c d].
    private static string ToKey(IReadOnlyList<DocumentSegment> segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment.Name is not { } name)
            {
                builder.Append('[').Append(segment.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            if (name.AsSpan().ContainsAnyExcept(PlainCharacters))
            {
                builder.Append('[').Append(name).Append(']');
            }
            else
            {
                builder.Append(name);
            }
        }

        return builder.ToString();
    }

    // Spring Boot does not bind a list with a missing index, so an item without a value (null, or a container without
    // any value) followed by an item with a value is invalid; a trailing one is left out.
    private static void CheckGaps(JToken token, string path)
    {
        switch (token)
        {
            case JObject container:
                foreach (var property in container.Properties())
                {
                    CheckGaps(property.Value, JsonPaths.Property(path, property.Name));
                }

                break;

            case JArray array:
                var last = array.Count - 1;

                while (last >= 0 && !HasValue(array[last]))
                {
                    last--;
                }

                for (var index = 0; index <= last; index++)
                {
                    var itemPath = JsonPaths.Item(path, index);

                    if (!HasValue(array[index]))
                    {
                        throw new InvalidDocumentException(
                            DocumentRules.ArrayGap,
                            itemPath,
                            $"The array item at {itemPath} has no value but a later item has one; a properties list cannot skip an index.");
                    }

                    CheckGaps(array[index], itemPath);
                }

                break;
        }
    }

    // Whether the token makes a key, as DocumentFlattener reads it.
    private static bool HasValue(JToken token)
    {
        return token switch
        {
            JObject container => container.PropertyValues().Any(HasValue),
            JArray array => array.Any(HasValue),
            JValue value => value.Type is not (JTokenType.Null or JTokenType.Undefined),
            _ => false,
        };
    }

    // In both: \, and \t, \n, \r, \f, and \uXXXX for every UTF-16 unit outside printable ASCII. In a key also the
    // characters that end it or start a comment; in a value a leading space, which a reader would skip.
    private static void AppendEscaped(StringBuilder builder, string text, bool isKey)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;

                case '\t':
                    builder.Append("\\t");
                    break;

                case '\n':
                    builder.Append("\\n");
                    break;

                case '\r':
                    builder.Append("\\r");
                    break;

                case '\f':
                    builder.Append("\\f");
                    break;

                case ' ' when isKey || index == 0:
                case '=' or ':' or '#' or '!' when isKey:
                    builder.Append('\\').Append(character);
                    break;

                case < ' ' or > '~':
                    builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    break;

                default:
                    builder.Append(character);
                    break;
            }
        }
    }
}
