using c0ded0c.Core;
using Microsoft.CodeAnalysis;

namespace c0ded0c.MsBuild
{
    public class HashPathCalculatorProvider : IPathCalculatorProvider
    {
        public PathCalculatorDelegate GetAssemblyPathCalculator(IAssemblySymbol assembly)
        {
            return (hash, _, _) => ArtifactExtensions.BuildPath(InfoNames.Assemblies, hash);
        }

        public PathCalculatorDelegate GetProjectPathCalculator()
        {
            return (hash, _, _) => ArtifactExtensions.BuildPath(InfoNames.Projects, hash);
        }

        public PathCalculatorDelegate GetDocumentPathCalculator(IIdentificator assemblyKey, string relativePath)
        {
            return (hash, _, _) => ArtifactExtensions.BuildPath(assemblyKey.Path, InfoNames.Documents, hash);
        }

        public PathCalculatorDelegate GetNamespacePathCalculator(IIdentificator assemblyKey)
        {
            return (hash, _, _) => ArtifactExtensions.BuildPath(assemblyKey.Path, InfoNames.Namespaces, hash);
        }

        public PathCalculatorDelegate GetTypePathCalculator(IIdentificator documentKey, IIdentificator assemblyKey)
        {
            return (hash, _, _) => ArtifactExtensions.BuildPath(assemblyKey.Path, InfoNames.Types, hash);
        }

        public PathCalculatorDelegate GetMemberPathCalculator(IIdentificator typeKey, IIdentificator documentKey, IIdentificator assemblyKey)
        {
            return (hash, _, _) => ArtifactExtensions.BuildPath(assemblyKey.Path, InfoNames.Members, hash);
        }
    }
}
