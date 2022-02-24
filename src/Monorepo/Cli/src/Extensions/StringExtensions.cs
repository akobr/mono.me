using System;
using System.Text;

namespace _42.Monorepo.Cli.Extensions
{
    public static class StringExtensions
    {
        public static bool EqualsOrdinalIgnoreCase(this string text, string? value)
        {
            return text.Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        public static bool EqualsOrdinal(this string text, string? value)
        {
            return text.Equals(value, StringComparison.Ordinal);
        }

        // TODO: [P3] Each section needs to be a valid identifier, starts with letter or _ (not only the first one)
        public static string ToValidItemName(this string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Trim());

            for (var i = 0; i < builder.Length; i++)
            {
                var character = builder[i];

                if (char.IsLetterOrDigit(character)
                    || character == '_'
                    || character == '.')
                {
                    continue;
                }

                builder[i] = '_';
            }

            if (builder.Length > 0
                && char.IsDigit(builder[0]))
            {
                builder.Insert(0, '_');
            }

            return builder.ToString();
        }

    }
}
