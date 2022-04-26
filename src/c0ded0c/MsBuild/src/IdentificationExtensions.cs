using System;
using System.IO;
using System.Linq;
using c0ded0c.Core;
using Microsoft.Build.Construction;
using Microsoft.CodeAnalysis;

namespace c0ded0c.MsBuild
{
    public static class IdentificationExtensions
    {
        private static readonly SymbolDisplayFormat TypeFullNameFormat = new SymbolDisplayFormat(
            typeQualificationStyle:
                SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        private static readonly SymbolDisplayFormat MemberFullNameFormat = new SymbolDisplayFormat(
            memberOptions:
                SymbolDisplayMemberOptions.IncludeParameters |
                SymbolDisplayMemberOptions.IncludeType |
                SymbolDisplayMemberOptions.IncludeExplicitInterface,
            parameterOptions:
                SymbolDisplayParameterOptions.IncludeType,
            genericsOptions:
                SymbolDisplayGenericsOptions.IncludeTypeParameters);

        public static IIdentificator? BuildKeyOfProjectFromFilePath(
            string filePath,
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder)
        {
            string? fileName = Path.GetFileName(filePath);

            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            return keyBuilder.Build(fileName, fileName, pathCalculatorProvider.GetProjectPathCalculator());
        }

        public static IIdentificator BuildKey(
            this ProjectInSolution project,
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder)
        {
            string projectFullPath = project.AbsolutePath;
            string projectName = Path.GetFileName(projectFullPath);
            return keyBuilder.Build(projectName, projectName, pathCalculatorProvider.GetProjectPathCalculator());
        }

        public static string BuildFullName(
            this Document document,
            IIdentificator assemblyKey)
        {
            string? projectDirectory = Path.GetDirectoryName(document.Project.FilePath);
            string relativePath = document.FilePath.GetRelativePath(projectDirectory).Replace('\\', '/');
            return ArtifactExtensions.BuildPath(assemblyKey.FullName, relativePath);
        }

        public static IIdentificator BuildKey(
            this Document document,
            IIdentificator assemblyKey,
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder)
        {
            string? projectDirectory = Path.GetDirectoryName(document.Project.FilePath);
            string relativePath = document.FilePath.GetRelativePath(projectDirectory).Replace('\\', '/');
            return keyBuilder.Build(
                ArtifactExtensions.BuildPath(assemblyKey.FullName, relativePath),
                document.Name,
                pathCalculatorProvider.GetDocumentPathCalculator(assemblyKey, relativePath));
        }

        public static IIdentificator BuildKey(
            this IAssemblySymbol assembly,
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder)
        {
            string fullName = GetAssemblyFullName(assembly);
            string name = assembly.Identity.Name;
            return keyBuilder.Build(fullName, name, pathCalculatorProvider.GetAssemblyPathCalculator(assembly));
        }

        public static string BuildFullNamespaceName(this INamespaceSymbol namespaceSymbol)
        {
            return namespaceSymbol.IsGlobalNamespace
                ? "global"
                : namespaceSymbol.ToDisplayString(TypeFullNameFormat);
        }

        public static IIdentificator BuildKey(
            this INamespaceSymbol namespaceSymbol,
            IIdentificator assemblyKey,
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder)
        {
            string namespaceFullName = BuildFullNamespaceName(namespaceSymbol);
            string fullName = $"{assemblyKey.FullName}/{namespaceFullName}";
            return keyBuilder.Build(fullName, namespaceFullName, pathCalculatorProvider.GetNamespacePathCalculator(assemblyKey));
        }

        public static IIdentificator BuildKey(
            this INamedTypeSymbol typeSymbol,
            IIdentificator documentKey,
            IIdentificator assemblyKey,
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder)
        {
            string typeFullName = typeSymbol.ToDisplayString(TypeFullNameFormat);
            string fullName = $"{assemblyKey.FullName}/{typeFullName}";
            return keyBuilder.Build(fullName, typeFullName, pathCalculatorProvider.GetTypePathCalculator(documentKey, assemblyKey));
        }

        public static IIdentificator BuildKey(
            this ISymbol memberSymbol,
            IIdentificator typeKey,
            IIdentificator documentKey,
            IIdentificator assemblyKey,
            IPathCalculatorProvider pathCalculatorProvider,
            IIdentificationBuilder keyBuilder)
        {
            // TODO: [P1] change this to name(params):type
            string memberFullName = GetMemberFullName(memberSymbol);
            string fullName = $"{typeKey.FullName}/{memberFullName}";
            return keyBuilder.Build(fullName, memberFullName, pathCalculatorProvider.GetMemberPathCalculator(typeKey, documentKey, assemblyKey));
        }

        // TODO: [P1] Refactor this
        private static string GetMemberFullName(ISymbol memberSymbol)
        {
            var parts = memberSymbol.ToDisplayParts(MemberFullNameFormat);
            int startIndex = 0;
            bool hasReturnType = false;

            // method (property) with return type
            if (parts.Length > 2 && parts[1].Kind == SymbolDisplayPartKind.Space)
            {
                hasReturnType = true;
                startIndex = 2;
            }

            // if return type is generic (but not a constructor)
            else if (parts.Length > 2 && parts[1].Kind == SymbolDisplayPartKind.Punctuation && parts[1].ToString() == "<")
            {
                hasReturnType = true;
                startIndex = parts.IndexOf(new SymbolDisplayPart(SymbolDisplayPartKind.Punctuation, null, ">")) + 1;
            }

            string fullName = string.Join(string.Empty, parts.Skip(startIndex).Where(p => p.Kind != SymbolDisplayPartKind.Space));

            // adds return type if it is not a void
            if (hasReturnType && parts[0].Kind != SymbolDisplayPartKind.Keyword)
            {
                fullName += $":{string.Join(string.Empty, parts.Take(startIndex).Where(p => p.Kind != SymbolDisplayPartKind.Space))}";
            }

            return fullName;
        }

        // TODO: [P4] Refactor this
        private static string GetAssemblyFullName(IAssemblySymbol assembly)
        {
            string fullName = $"{assembly.Identity.Name},v.{assembly.Identity.Version}";

            if (assembly.Identity.HasPublicKey)
            {
                byte[] key = new byte[assembly.Identity.PublicKey.Length];
                assembly.Identity.PublicKey.CopyTo(key);
                fullName += $",{string.Concat(Array.ConvertAll(key, x => x.ToString("x2")))}";
            }
            else if (!assembly.Identity.PublicKeyToken.IsDefaultOrEmpty)
            {
                byte[] key = new byte[assembly.Identity.PublicKeyToken.Length];
                assembly.Identity.PublicKeyToken.CopyTo(key);
                fullName += $",{string.Concat(Array.ConvertAll(key, x => x.ToString("x2")))}";
            }

            return fullName;
        }
    }
}
