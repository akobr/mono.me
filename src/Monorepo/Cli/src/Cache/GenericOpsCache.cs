using System.Collections.Concurrent;
using _42.Monorepo.Cli.Model;

namespace _42.Monorepo.Cli.Cache
{
    public class GenericOpsCache : IGenericOpsCache
    {
        private readonly ConcurrentDictionary<IIdentifier, IGenericCacheItem> cache = new();

        public IGenericCacheItem GetOrAddItem(IIdentifier key)
        {
            return cache.GetOrAdd(key, _ => new GenericCacheItem());
        }
    }
}
