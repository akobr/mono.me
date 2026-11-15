using System;
using System.Globalization;
using System.Text;

namespace _42.Platform.Storyteller;

// The text rules of project settings that every runtime applies the same way: case folds in ASCII only (A-Z to a-z, so
// no runtime folds a long s or a Kelvin sign into a code), and JSON paths are printed as the conformance suite prints
// them ($, .name for a plain name, ['name'] for any other, as JsonPaths of ConfigurationFormats).
internal static class ProjectSettingsText
{
    public const string Root = "$";

    public static string Fold(string value)
    {
        if (!value.AsSpan().ContainsAnyInRange('A', 'Z'))
        {
            return value;
        }

        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                span[index] = character is >= 'A' and <= 'Z' ? (char)(character + ('a' - 'A')) : character;
            }
        });
    }

    public static bool EqualsIgnoreCase(string left, string right)
    {
        return string.Equals(Fold(left), Fold(right), StringComparison.Ordinal);
    }

    public static string Property(string parent, string name)
    {
        return IsPlain(name)
            ? $"{parent}.{name}"
            : $"{parent}['{Escape(name)}']";
    }

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

                case < ' ' or '\u007F' or '#':
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
