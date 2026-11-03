using System.Text;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests.Readers;

// Reads the .env text the formatter writes as the key/value import does (KeyValueFormats.ParseEnv in sform), keeping
// the names: a bare value, a single-quoted value taken literally, or a double-quoted value with \\, \", \n, \r and \t.
// It is strict: a bare value with any other character, or text after the closing quote, fails.
internal static class EnvReader
{
    private const string BareCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.,:/@%+-";

    public static IReadOnlyList<(string Name, string Value)> Read(string text)
    {
        var entries = new List<(string Name, string Value)>();

        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            separator.ShouldBeGreaterThan(0, line);
            entries.Add((line[..separator], ReadValue(line[(separator + 1)..])));
        }

        return entries;
    }

    private static string ReadValue(string text)
    {
        if (text.StartsWith('\''))
        {
            text.Length.ShouldBeGreaterThan(1);
            text[^1].ShouldBe('\'');
            text[1..^1].ShouldNotContain('\'');
            return text[1..^1];
        }

        if (!text.StartsWith('"'))
        {
            text.ShouldAllBe(character => BareCharacters.Contains(character));
            return text;
        }

        var builder = new StringBuilder();
        var index = 1;

        for (; text[index] != '"'; index++)
        {
            if (text[index] != '\\')
            {
                builder.Append(text[index]);
                continue;
            }

            index++;
            builder.Append(text[index] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                var other => other,
            });
        }

        index.ShouldBe(text.Length - 1);
        return builder.ToString();
    }
}
