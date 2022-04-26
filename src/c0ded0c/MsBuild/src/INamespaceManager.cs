using System.Collections.Generic;
using c0ded0c.Core;
using Microsoft.CodeAnalysis;

namespace c0ded0c.MsBuild
{
    public interface INamespaceManager
    {
        NamespaceInfo.Builder GetOrSetNamespace(INamespaceSymbol @namespace, IIdentificator assemblyKey);

        IEnumerable<KeyValuePair<string, INamespaceInfo>> GetNamespaceMap();
    }
}
