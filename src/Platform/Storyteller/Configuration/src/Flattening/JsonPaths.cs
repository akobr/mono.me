using System.Text;

namespace _42.Platform.Storyteller.Sdk.Configuring.Flattening;

// JSON paths for messages and test vectors, the same in every language: $ for the root, .name for a plain property
// name, ['name'] for any other, [0] for an array item.
internal static class JsonPaths
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

    // Letters, digits, _ and -, starting with a letter or _.
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

    private static string Escape(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            if (character is '\\' or '\'')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
