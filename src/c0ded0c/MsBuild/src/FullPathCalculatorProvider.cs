using c0ded0c.Core;
using Microsoft.CodeAnalysis;

namespace c0ded0c.MsBuild
{
    public class FullPathCalculatorProvider : IPathCalculatorProvider
    {
        public PathCalculatorDelegate GetAssemblyPathCalculator(IAssemblySymbol assembly)
        {
            return (_, _, name) => ArtifactExtensions.BuildPath(InfoNames.Assemblies, name, assembly.Identity.Version.ToString());
        }

        public PathCalculatorDelegate GetProjectPathCalculator()
        {
            return (_, _, name) => ArtifactExtensions.BuildPath(InfoNames.Projects, name);
        }

        public PathCalculatorDelegate GetDocumentPathCalculator(IIdentificator assemblyKey, string relativePath)
        {
            relativePath = relativePath.Replace("..", "$up$");
            return (_, _, name) => ArtifactExtensions.BuildPath(assemblyKey.Path, InfoNames.Documents, relativePath);
        }

        public PathCalculatorDelegate GetNamespacePathCalculator(IIdentificator assemblyKey)
        {
            return (_, _, name) => ArtifactExtensions.BuildPath(assemblyKey.Path, InfoNames.Namespaces, name);
        }

        public PathCalculatorDelegate GetTypePathCalculator(IIdentificator documentKey, IIdentificator assemblyKey)
        {
            return (_, _, name) => ArtifactExtensions.BuildPath(assemblyKey.Path, InfoNames.Types, name);
        }

        public PathCalculatorDelegate GetMemberPathCalculator(IIdentificator typeKey, IIdentificator documentKey, IIdentificator assemblyKey)
        {
            return (_, _, name) => ArtifactExtensions.BuildPath(typeKey.Path, name);
        }
    }
}
