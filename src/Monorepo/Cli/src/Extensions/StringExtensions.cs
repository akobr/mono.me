using System;
using System.Collections.Generic;
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

        public static string ToValidItemName(this string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Trim());
            var dotIndexes = new Stack<int>();
            var lastDotIndex = -1;

            for (var i = 0; i < builder.Length; i++)
            {
                var character = builder[i];

                if (character is '.')
                {
                    if (lastDotIndex == i - 1)
                    {
                        lastDotIndex = i;
                        builder[i] = '_';
                        continue;
                    }

                    lastDotIndex = i;
                    dotIndexes.Push(i);
                    continue;
                }

                if (char.IsLetterOrDigit(character)
                    || character is '_')
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
