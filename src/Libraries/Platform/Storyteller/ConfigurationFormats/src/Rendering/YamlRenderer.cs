using System.Buffers;
using System.Globalization;
using System.Text;

namespace _42.Platform.Storyteller.ConfigurationFormats.Rendering;

// YAML 1.2 written from the leaves, without a YAML library: the nested document in document order, two-space
// indentation, block sequences under their key, every string double-quoted, numbers and booleans plain. A gap in an
// array is null, so every other item keeps its index; a trailing item without a value and a container without any
// value are left out. Flattening the parsed YAML gives exactly the flat keys.
internal static class YamlRenderer
{
    // The longest implicit key YAML allows; a longer one is written as an explicit key, ? key.
    private const int MaxImplicitKeyLength = 1024;

    private static readonly SearchValues<char> KeyCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");

    // A YAML 1.1 reader takes these for a boolean or null, also as a key.
    private static readonly HashSet<string> ReservedKeys =
        new(["y", "yes", "n", "no", "true", "false", "on", "off", "null"], StringComparer.OrdinalIgnoreCase);

    public static string Render(IReadOnlyList<DocumentLeaf> leaves)
    {
        if (leaves.Count == 0)
        {
            return "{}\n";
        }

        var root = new Node();

        foreach (var leaf in leaves)
        {
            Add(root, leaf);
        }

        var builder = new StringBuilder();
        WriteMapping(builder, root, 0, false);
        return builder.ToString();
    }

    // The leaves of one container are contiguous in document order, so the node of a step is the last child or a new
    // one.
    private static void Add(Node root, DocumentLeaf leaf)
    {
        var node = root;

        foreach (var segment in leaf.Segments)
        {
            if (node.Children.Count == 0 || node.Children[^1].Segment != segment)
            {
                node.Children.Add((segment, new Node()));
            }

            node = node.Children[^1].Node;
        }

        node.Leaf = leaf;
    }

    private static void WriteContainer(StringBuilder builder, Node node, int indent, bool inline)
    {
        if (node.Children[0].Segment.IsIndex)
        {
            WriteSequence(builder, node, indent, inline);
        }
        else
        {
            WriteMapping(builder, node, indent, inline);
        }
    }

    // inline: the first entry continues a line that already has its "- ".
    private static void WriteMapping(StringBuilder builder, Node node, int indent, bool inline)
    {
        foreach (var (segment, child) in node.Children)
        {
            StartLine(builder, indent, ref inline);
            var key = FormatKey(segment.Name!);

            if (key.Length > MaxImplicitKeyLength)
            {
                builder.Append("? ").Append(key).Append('\n').Append(' ', indent);
            }
            else
            {
                builder.Append(key);
            }

            builder.Append(':');

            if (child.Leaf is { } leaf)
            {
                builder.Append(' ');
                AppendScalar(builder, leaf);
                builder.Append('\n');
            }
            else
            {
                builder.Append('\n');
                WriteContainer(builder, child, indent + 2, false);
            }
        }
    }

    private static void WriteSequence(StringBuilder builder, Node node, int indent, bool inline)
    {
        var next = 0;

        foreach (var (segment, child) in node.Children)
        {
            // A gap: items without a value before one with a value.
            for (; next < segment.Index; next++)
            {
                StartLine(builder, indent, ref inline);
                builder.Append("- null\n");
            }

            StartLine(builder, indent, ref inline);
            builder.Append("- ");

            if (child.Leaf is { } leaf)
            {
                AppendScalar(builder, leaf);
                builder.Append('\n');
            }
            else
            {
                WriteContainer(builder, child, indent + 2, true);
            }

            next++;
        }
    }

    private static void StartLine(StringBuilder builder, int indent, ref bool inline)
    {
        if (inline)
        {
            inline = false;
            return;
        }

        builder.Append(' ', indent);
    }

    // The JSON text of a number or a boolean is a YAML 1.2 number or boolean.
    private static void AppendScalar(StringBuilder builder, DocumentLeaf leaf)
    {
        if (leaf.Kind == DocumentValueKind.String)
        {
            AppendQuoted(builder, leaf.Value);
        }
        else
        {
            builder.Append(leaf.Value);
        }
    }

    // Plain when it matches [A-Za-z_][A-Za-z0-9_-]* and no reader takes it for a boolean or null; otherwise quoted.
    private static string FormatKey(string name)
    {
        if ((char.IsAsciiLetter(name[0]) || name[0] == '_')
            && !name.AsSpan().ContainsAnyExcept(KeyCharacters)
            && !ReservedKeys.Contains(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 2);
        AppendQuoted(builder, name);
        return builder.ToString();
    }

    // The JSON escapes are YAML escapes too; \uXXXX for every character YAML does not print raw (C0, DEL, C1 with the
    // next line, the line and paragraph separators) and for the byte order mark and the non-characters U+FFFE, U+FFFF.
    private static void AppendQuoted(StringBuilder builder, string text)
    {
        builder.Append('"');

        foreach (var character in text)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;

                case '\\':
                    builder.Append("\\\\");
                    break;

                case '\n':
                    builder.Append("\\n");
                    break;

                case '\r':
                    builder.Append("\\r");
                    break;

                case '\t':
                    builder.Append("\\t");
                    break;

                case < ' ' or (>= '\u007F' and <= '\u009F') or '\u2028' or '\u2029' or '\uFEFF' or '\uFFFE' or '\uFFFF':
                    builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    break;

                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
    }

    // A mapping or a sequence (by the kind of its steps), or a value.
    private sealed class Node
    {
        public List<(DocumentSegment Segment, Node Node)> Children { get; } = [];

        public DocumentLeaf? Leaf { get; set; }
    }
}
