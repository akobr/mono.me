using System.Globalization;
using System.Text;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests.Readers;

// Reads a .properties text as java.util.Properties.load does, for the lines the formatter writes (no comments and no
// continuation lines): the key ends at the first unescaped =, : or whitespace, the separator and the whitespace around
// it are skipped, and \t, \n, \r, \f and \uXXXX are escapes; any other escaped character is itself.
internal static class PropertiesReader
{
    public static IReadOnlyList<(string Key, string Value)> Read(string text)
    {
        var entries = new List<(string Key, string Value)>();

        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var index = 0;
            var key = ReadPart(line, ref index, true);
            SkipWhitespace(line, ref index);

            if (index < line.Length && line[index] is '=' or ':')
            {
                index++;
            }

            SkipWhitespace(line, ref index);
            entries.Add((key, ReadPart(line, ref index, false)));
        }

        return entries;
    }

    private static void SkipWhitespace(string line, ref int index)
    {
        while (index < line.Length && line[index] is ' ' or '\t' or '\f')
        {
            index++;
        }
    }

    private static string ReadPart(string line, ref int index, bool isKey)
    {
        var builder = new StringBuilder();

        while (index < line.Length)
        {
            var character = line[index];

            if (isKey && character is '=' or ':' or ' ' or '\t' or '\f')
            {
                break;
            }

            index++;

            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            var escaped = line[index++];

            if (escaped == 'u')
            {
                builder.Append((char)int.Parse(line.AsSpan(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                index += 4;
                continue;
            }

            builder.Append(escaped switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                'f' => '\f',
                _ => escaped,
            });
        }

        return builder.ToString();
    }
}
