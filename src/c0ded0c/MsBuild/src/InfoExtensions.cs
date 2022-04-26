using System.IO;
using c0ded0c.Core;
using Microsoft.Build.Construction;
using Microsoft.CodeAnalysis;

namespace c0ded0c.MsBuild
{
    public static class InfoExtensions
    {
        public static Core.ProjectInfo? BuildInfoOfProjectFromFilePath(
            string filePath,
            IPathCalculatorProvider pathCalculationProvider,
            IIdentificationBuilder keyBuilder)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            IIdentificator? identificator = IdentificationExtensions.BuildKeyOfProjectFromFilePath(filePath, pathCalculationProvider, keyBuilder);
            if (identificator == null)
            {
                return null;
            }

            string fullPath = Path.GetFullPath(filePath);
            return new Core.ProjectInfo(identificator, fullPath);
        }


        public static Core.ProjectInfo BuildInfo(
            this ProjectInSolution project,
            IPathCalculatorProvider pathCalculationProvider,
            IIdentificationBuilder keyBuilder)
        {
            return new Core.ProjectInfo(
                project.BuildKey(pathCalculationProvider, keyBuilder),
                project.AbsolutePath)
            {
                MutableTag = project,
            };
        }

        public static Core.DocumentInfo BuildInfo(
            this Document document,
            IIdentificator assemblyKey,
            IPathCalculatorProvider pathCalculationProvider,
            IIdentificationBuilder keyBuilder)
        {
            return new Core.DocumentInfo(
                document.BuildKey(
                    assemblyKey,
                    pathCalculationProvider,
                    keyBuilder))
            {
                MutableTag = document,
            };
        }

        public static AssemblyInfo BuildInfo(
            this IAssemblySymbol assemblySymbol,
            IPathCalculatorProvider pathCalculationProvider,
            IIdentificationBuilder keyBuilder)
        {
            return new AssemblyInfo(assemblySymbol.BuildKey(pathCalculationProvider, keyBuilder))
            {
                MutableTag = assemblySymbol,
            };
        }

        public static NamespaceInfo BuildInfo(
            this INamespaceSymbol namespaceSymbol,
            IIdentificator assemblyKey,
            IPathCalculatorProvider pathCalculationProvider,
            IIdentificationBuilder keyBuilder)
        {
            return new NamespaceInfo(namespaceSymbol.BuildKey(assemblyKey, pathCalculationProvider, keyBuilder))
            {
                MutableTag = namespaceSymbol,
            };
        }

        public static Core.TypeInfo BuildInfo(
            this INamedTypeSymbol typeSymbol,
            IIdentificator documentKey,
            IIdentificator assemblyKey,
            IPathCalculatorProvider pathCalculationProvider,
            IIdentificationBuilder keyBuilder)
        {
            return new Core.TypeInfo(typeSymbol.BuildKey(documentKey, assemblyKey, pathCalculationProvider, keyBuilder))
            {
                MutableTag = typeSymbol,
            };
        }

        public static MemberInfo BuildInfo(
            this ISymbol memberSymbol,
            IIdentificator typeKey,
            IIdentificator documentKey,
            IIdentificator assemblyKey,
            IPathCalculatorProvider pathCalculationProvider,
            IIdentificationBuilder keyBuilder)
        {
            return new MemberInfo(memberSymbol.BuildKey(typeKey, documentKey, assemblyKey, pathCalculationProvider, keyBuilder))
            {
                MutableTag = memberSymbol,
            };
        }
    }
}
