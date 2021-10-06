using System;
using System.Collections.Concurrent;

namespace _42.Monorepo.Cli.Cache
{
    public class GenericCacheItem : IGenericCacheItem
    {
        private readonly ConcurrentDictionary<string, object> values;

        public GenericCacheItem()
        {
            values = new ConcurrentDictionary<string, object>();
        }

        public bool ContainsValue(string cacheKey)
        {
            return values.ContainsKey(cacheKey);
        }

        public AsyncLazy<T> GetValue<T>(string cacheKey)
        {
            if (!values.TryGetValue(cacheKey, out var rawValue))
            {
                throw new InvalidOperationException($"The value of '{cacheKey}' is not present in cache item.");
            }

            return (AsyncLazy<T>)rawValue;
        }

        public AsyncLazy<T> GetOrAddValue<T>(string cacheKey, Func<string, AsyncLazy<T>> valueFactory)
        {
            return (AsyncLazy<T>)values.GetOrAdd(cacheKey, valueFactory);
        }

        public void RegisterValue<T>(string key, AsyncLazy<T> value)
        {
            values.TryAdd(key, value);
        }
    }
}
