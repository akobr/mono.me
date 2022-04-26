using System;
using System.Collections.Generic;
using System.Linq;

namespace c0ded0c.Core
{
    public static class PropertiesExtensions
    {
        public static TValue Get<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> properties, TKey key, TValue defaultValue = default)
        {
            return properties.TryGetValue(key, out TValue value)
                ? value
                : defaultValue;
        }

        public static bool IsSet<TKey>(this IReadOnlyDictionary<TKey, string> properties, TKey key)
        {
            return properties.TryGetValue(key, out string flagValue)
                   && bool.TryParse(flagValue, out bool boolValue)
                    ? boolValue
                    : false;
        }

        public static IEnumerable<string> GetEnumerable<TKey>(this IReadOnlyDictionary<TKey, string> properties, TKey key)
        {
            return properties.TryGetValue(key, out string joinedValue)
                ? joinedValue.Split(Constants.LIST_SEPARATOR, StringSplitOptions.RemoveEmptyEntries)
                : Enumerable.Empty<string>();
        }
    }
}
