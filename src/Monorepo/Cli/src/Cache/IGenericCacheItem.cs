using System;

namespace _42.Monorepo.Cli.Cache
{
    public interface IGenericCacheItem
    {
        bool ContainsValue(string cacheKey);

        AsyncLazy<T> GetValue<T>(string cacheKey);

        AsyncLazy<T> GetOrAddValue<T>(string cacheKey, Func<string, AsyncLazy<T>> valueFactory);

        void RegisterValue<T>(string key, AsyncLazy<T> value);
    }
}
