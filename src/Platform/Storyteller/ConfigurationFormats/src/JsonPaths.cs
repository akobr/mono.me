using System.Globalization;
using System.Text;

namespace _42.Platform.Storyteller.ConfigurationFormats;

// JSON paths for messages and test vectors, the same in every language: $ for the root, .name for a plain property
// name, ['name'] for any other, [0] for an array item.
public static class JsonPaths
{
    public const string Root = "$";

    public static string Property(string parent, string name)
    {
        return IsPlain(name)
            ? $"{parent}.{name}"
            : $"{parent}['{Escape(name)}']";
    }

    public static string Item(string parent, int index)
    {
        return $"{parent}[{index}]";
    }

    // ASCII letters, digits, _ and -, starting with an ASCII letter or _.
    private static bool IsPlain(string name)
    {
        if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    // \ and ' with a backslash, and C0 control characters and DEL as \n, \r, \t or \uXXXX, so a crafted name cannot put
    // a CR or an LF, and with it a GitHub workflow command, into a message, a log line or stderr. C1 controls, U+2028
    // and U+2029 are kept, as the vectors pin for every language.
    private static string Escape(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            switch (character)
            {
                case '\\' or '\'':
                    builder.Append('\\').Append(character);
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

                case < ' ' or '\u007F':
                    builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    break;

                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}
