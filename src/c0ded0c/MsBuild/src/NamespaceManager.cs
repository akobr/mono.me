using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using c0ded0c.Core;
using Microsoft.CodeAnalysis;

namespace c0ded0c.MsBuild
{
    public class NamespaceManager : INamespaceManager
    {
        private readonly ConcurrentDictionary<string, NamespaceInfo.Builder> namespaces;
        private readonly IPathCalculatorProvider pathCalculatorProvider;
        private readonly IIdentificationBuilder keyBuilder;

        public NamespaceManager(
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder)
        {
            this.pathCalculatorProvider = pathCalculatorProvider;
            this.keyBuilder = keyBuilder;
            namespaces = new ConcurrentDictionary<string, NamespaceInfo.Builder>();
        }

        public NamespaceInfo.Builder GetOrSetNamespace(INamespaceSymbol @namespace, IIdentificator assemblyKey)
        {
            string fullName = $"{assemblyKey.FullName}/{@namespace.BuildFullNamespaceName()}";
            return namespaces.GetOrAdd(fullName, (fullName) => @namespace.BuildInfo(assemblyKey, pathCalculatorProvider, keyBuilder).ToBuilder());
        }

        public IEnumerable<KeyValuePair<string, INamespaceInfo>> GetNamespaceMap()
        {
            return namespaces.Select(kvp => KeyValuePair.Create<string, INamespaceInfo>(kvp.Key, kvp.Value.ToImmutable()));
        }
    }
}
