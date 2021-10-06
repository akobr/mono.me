using System;
using _42.Monorepo.Cli.Model;

namespace _42.Monorepo.Cli.Cache
{
    public interface IGenericOpsCache
    {
        IGenericCacheItem GetOrAddItem(IIdentifier key);
    }
}
