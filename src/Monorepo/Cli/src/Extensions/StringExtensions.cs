using System;

namespace _42.Monorepo.Cli.Extensions
{
    public static class StringExtensions
    {
        public static bool EqualsOrdinalIgnoreCase(this string text, string value)
        {
            return text.Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        public static bool EqualsOrdinal(this string text, string value)
        {
            return text.Equals(value, StringComparison.Ordinal);
        }
    }
}
