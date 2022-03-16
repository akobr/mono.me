using System.Collections.Generic;

namespace _42.Monorepo.Cli.Extensions;

public static class DictionaryExtensions
{
    public static void MergeInto<TMap, TKey, TValue>(this TMap @this, params IReadOnlyDictionary<TKey, TValue>[] others)
        where TMap : IDictionary<TKey, TValue>
    {
        foreach (var src in others)
        {
            foreach (var (key, value) in src)
            {
                @this[key] = value;
            }
        }
    }

    public static Dictionary<TKey, TValue> MergeLeft<TKey, TValue>(params IReadOnlyDictionary<TKey, TValue>[] others)
        where TKey : notnull
    {
        var map = new Dictionary<TKey, TValue>();
        map.MergeInto(others);
        return map;
    }
}
